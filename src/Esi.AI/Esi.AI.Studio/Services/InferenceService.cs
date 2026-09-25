using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Runs one normalized chat generation against the selected local runtime.</summary>
public interface IInferenceService
{
    Task<GenerationResult> GenerateAsync(
        PersistedChat chat,
        ChatExchangeRequest request,
        string backend,
        Func<string, Task>? onDelta = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Serializes model generation work across the process-local runtime set.</summary>
public interface IInferenceScheduler
{
    Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
}

/// <summary>Provides bounded process-local scheduling for model generation.</summary>
public sealed class InferenceScheduler : IInferenceScheduler, IDisposable
{
    private readonly SemaphoreSlim generationSlots = new(1, 1);

    /// <inheritdoc />
    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await generationSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            generationSlots.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => generationSlots.Dispose();
}

/// <summary>Coordinates backend selection, multimodal preparation, and token generation.</summary>
public sealed class InferenceService(
    ModelRuntime modelRuntime,
    IInferenceScheduler scheduler,
    IInferenceFailureCoordinator? failureCoordinator = null) : IInferenceService
{
    private readonly IInferenceFailureCoordinator effectiveFailureCoordinator = failureCoordinator ?? NoOpInferenceFailureCoordinator.Instance;

    /// <inheritdoc />
    public Task<GenerationResult> GenerateAsync(
        PersistedChat chat,
        ChatExchangeRequest request,
        string backend,
        Func<string, Task>? onDelta = null,
        CancellationToken cancellationToken = default) =>
        scheduler.RunAsync(
            () => GenerateCoreAsync(chat, request, backend, onDelta, cancellationToken),
            cancellationToken);

    private async Task<GenerationResult> GenerateCoreAsync(
        PersistedChat chat,
        ChatExchangeRequest request,
        string backend,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();
        var messages = chat.Messages.Select(message => new ChatMessage(message.Role, message.Content))
            .Append(new ChatMessage("user", content, request.Images, request.ContentParts)).ToArray();
        OpenVinoModelLoadStatus? openVinoStatus = null;
        if (string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase))
        {
            var modelPath = Path.GetFullPath(request.ModelPath!);
            if (string.IsNullOrWhiteSpace(modelRuntime.GetLoadedBackendVariantId(modelPath)))
            {
                openVinoStatus = modelRuntime.GetOpenVinoStatus();
                if (!openVinoStatus.IsModelLoaded)
                    throw new InvalidOperationException("The selected OpenVINO model is not loaded.");
                if (!string.Equals(openVinoStatus.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The selected OpenVINO model does not match the loaded model. Loaded: '{openVinoStatus.ModelPath}', selected: '{modelPath}'.");
            }
        }

        if (request.Images is { Count: > 0 } && !modelRuntime.SupportsImageInput(backend, request.ModelPath))
            throw new InvalidOperationException($"The {backend} backend does not support image input.");
        if (!string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backend, "vLLM", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backend, "SGLang", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backend, "dotLLM", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetExtension(request.ModelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("LLama chat requires a .gguf model path.", nameof(request));

        try
        {
            var backendVariantId = modelRuntime.GetLoadedBackendVariantId(request.ModelPath);
            if (!string.IsNullOrWhiteSpace(backendVariantId))
            {
                var normalizedRequest = new OpenAiBackendChatRequest(
                    backend,
                    Path.GetFileNameWithoutExtension(request.ModelPath!),
                    request.ModelPath,
                    messages.Select(message => new OpenAiChatMessage(
                        message.Role,
                        message.Content,
                        message.ToolCalls,
                        message.ToolCallId)).ToArray(),
                    messages,
                    null,
                    new ChatGenerationOptions(),
                    BackendVariantId: backendVariantId);
                return await modelRuntime.GenerateBackendAsync(normalizedRequest, onDelta, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase))
            {
                using var openVinoSession = modelRuntime.CreateOpenVinoChatSession();
                var imageTensors = OpenVinoImageTensorFactory.Create(messages);
                try
                {
                    var openVinoGeneration = openVinoSession.GenerateWithStats(messages, streamer: delta =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        onDelta?.Invoke(delta).GetAwaiter().GetResult();
                    }, images: imageTensors.Length == 0 ? null : imageTensors);
                    return new GenerationResult(openVinoGeneration.Text, openVinoGeneration.TokenCount, TimeSpan.Zero, openVinoGeneration.TokensPerSecond, openVinoGeneration.PromptTokenCount, "stop", null, openVinoGeneration.TimeToFirstTokenMs, openVinoGeneration.PrefillDurationMs, openVinoGeneration.DecodeDurationMs);
                }
                finally
                {
                    foreach (var imageTensor in imageTensors)
                        imageTensor.Dispose();
                }
            }

            if (string.Equals(backend, "vLLM", StringComparison.OrdinalIgnoreCase) || string.Equals(backend, "SGLang", StringComparison.OrdinalIgnoreCase))
            {
                using var pythonSession = modelRuntime.CreatePythonChatSession();
                return await pythonSession.GenerateWithStatsAsync(messages, onDelta, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(backend, "dotLLM", StringComparison.OrdinalIgnoreCase))
            {
                using var dotLlmSession = modelRuntime.CreateDotLlmChatSession();
                return await dotLlmSession.GenerateWithStatsAsync(messages, onDelta, cancellationToken).ConfigureAwait(false);
            }

            using var session = modelRuntime.CreateLlamaChatSession("You are a helpful assistant.", request.ModelPath);
            return await session.GenerateWithStatsAsync(messages, onDelta, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(request))
        {
            throw;
        }
        catch (Exception exception)
        {
            await effectiveFailureCoordinator.FailAsync(exception).ConfigureAwait(false);
            throw;
        }
    }
}
