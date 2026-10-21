using System.Diagnostics;
using System.Text;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    IInferenceFailureCoordinator? failureCoordinator = null,
    ILogger<InferenceService>? logger = null,
    InferenceLogService? inferenceLogService = null) : IInferenceService
{
    private readonly IInferenceFailureCoordinator effectiveFailureCoordinator = failureCoordinator ?? NoOpInferenceFailureCoordinator.Instance;
    private readonly ILogger<InferenceService> effectiveLogger = logger ?? NullLogger<InferenceService>.Instance;

    /// <inheritdoc />
    public async Task<GenerationResult> GenerateAsync(
        PersistedChat chat,
        ChatExchangeRequest request,
        string backend,
        Func<string, Task>? onDelta = null,
        CancellationToken cancellationToken = default)
    {
        var inferenceId = $"inf-{Guid.NewGuid():N}";
        var imageCount = request.Images?.Count ?? 0;
        var messageCount = chat.Messages.Count + 1;
        var modelName = string.IsNullOrWhiteSpace(request.ModelPath) ? "unknown" : Path.GetFileNameWithoutExtension(request.ModelPath);
        using var scope = effectiveLogger.BeginScope(new Dictionary<string, object?>
        {
            ["InferenceId"] = inferenceId,
            ["InferencePath"] = "SignalR",
            ["Backend"] = backend,
            ["Model"] = modelName,
            ["BackendVariantId"] = request.BackendVariantId,
            ["MessageCount"] = messageCount,
            ["ImageCount"] = imageCount,
            ["ToolCount"] = 0,
            ["Streaming"] = onDelta is not null
        });
        var stopwatch = Stopwatch.StartNew();
        effectiveLogger.LogInformation(
            "Inference {InferenceId} started via SignalR for backend {Backend}, model {Model}, {MessageCount} messages, {ImageCount} images, {ToolCount} tools; streaming={Streaming}.",
            inferenceId,
            backend,
            modelName,
            messageCount,
            imageCount,
            0,
            onDelta is not null);

        InferenceLog? inferenceLog = null;
        Exception? fatalBackendFailure = null;
        GenerationResult result;
        try
        {
            if (inferenceLogService is not null)
            {
                var text = string.Join("\n", chat.Messages.Select(message => message.Content).Append(request.Content));
                var estimatedPrefillTokens = Math.Max(1, (Encoding.UTF8.GetByteCount(text) + 3) / 4);
                inferenceLog = await inferenceLogService.InferenceLog_CreateAsync(
                    new InferenceLog(
                        Guid.NewGuid(),
                        inferenceId,
                        DateTimeOffset.UtcNow,
                        "SignalR",
                        backend,
                        modelName,
                        request.BackendVariantId,
                        messageCount,
                        imageCount,
                        0,
                        onDelta is not null,
                        InferenceLogStatus.Running,
                        estimatedPrefillTokens),
                    cancellationToken).ConfigureAwait(false);
            }

            result = await scheduler.RunAsync(
                () => GenerateCoreAsync(chat, request, backend, onDelta, inferenceId, exception => fatalBackendFailure = exception, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            effectiveLogger.LogInformation("Inference {InferenceId} canceled after {ElapsedMilliseconds} ms.", inferenceId, stopwatch.ElapsedMilliseconds);
            if (inferenceLog is not null && inferenceLogService is not null)
            {
                await inferenceLogService.InferenceLog_UpdateAsync(
                    inferenceLog with
                    {
                        Status = InferenceLogStatus.Canceled,
                        CompletedAtUtc = DateTimeOffset.UtcNow,
                        DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                        ErrorType = nameof(OperationCanceledException),
                        ErrorMessage = "The caller cancelled the inference request."
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception exception)
        {
            effectiveLogger.LogError(exception, "Inference {InferenceId} failed after {ElapsedMilliseconds} ms.", inferenceId, stopwatch.ElapsedMilliseconds);
            try
            {
                if (inferenceLog is not null && inferenceLogService is not null)
                {
                    await inferenceLogService.InferenceLog_UpdateAsync(
                        inferenceLog with
                        {
                            Status = InferenceLogStatus.Failed,
                            CompletedAtUtc = DateTimeOffset.UtcNow,
                            DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                            ErrorType = exception.GetType().FullName ?? exception.GetType().Name,
                            ErrorMessage = exception.Message
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                if (fatalBackendFailure is not null)
                    await effectiveFailureCoordinator.FailAsync(fatalBackendFailure, inferenceId).ConfigureAwait(false);
            }

            throw;
        }

        if (inferenceLog is not null && inferenceLogService is not null)
        {
            await inferenceLogService.InferenceLog_UpdateAsync(
                inferenceLog with
                {
                    Status = InferenceLogStatus.Succeeded,
                    OutputTokenCount = result.TokenCount,
                    FinishReason = result.FinishReason,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    DurationMilliseconds = stopwatch.ElapsedMilliseconds
                },
                CancellationToken.None).ConfigureAwait(false);
        }

        effectiveLogger.LogInformation(
            "Inference {InferenceId} completed after {ElapsedMilliseconds} ms with {TokenCount} tokens and finish reason {FinishReason}.",
            inferenceId,
            stopwatch.ElapsedMilliseconds,
            result.TokenCount,
            result.FinishReason);
        return result;
    }

    private async Task<GenerationResult> GenerateCoreAsync(
        PersistedChat chat,
        ChatExchangeRequest request,
        string backend,
        Func<string, Task>? onDelta,
        string inferenceId,
        Action<Exception> markFatalBackendFailure,
        CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();
        var messages = chat.Messages.Select(message => new ChatMessage(message.Role, message.Content))
            .Append(new ChatMessage("user", content, request.Images, request.ContentParts)).ToArray();
        var loadedBackendVariantId = modelRuntime.GetLoadedBackendVariantId(request.ModelPath, request.BackendVariantId);
        if (!string.IsNullOrWhiteSpace(request.BackendVariantId) &&
            modelRuntime.HasBackendRuntime(request.BackendVariantId) &&
            string.IsNullOrWhiteSpace(loadedBackendVariantId))
            throw new InvalidOperationException($"The selected backend variant '{request.BackendVariantId}' is not loaded for model '{request.ModelPath}'.");

        OpenVinoModelLoadStatus? openVinoStatus = null;
        if (string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase))
        {
            var modelPath = Path.GetFullPath(request.ModelPath!);
            if (string.IsNullOrWhiteSpace(loadedBackendVariantId))
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
        if (string.IsNullOrWhiteSpace(loadedBackendVariantId) &&
            !string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backend, "vLLM", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetExtension(request.ModelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("LLama chat requires a .gguf model path.", nameof(request));

        try
        {
            if (!string.IsNullOrWhiteSpace(loadedBackendVariantId))
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
                    BackendVariantId: loadedBackendVariantId);
                return await modelRuntime.GenerateBackendAsync(normalizedRequest, onDelta, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase))
            {
                using var openVinoSession = modelRuntime.CreateOpenVinoChatSession();
                var imageTensors = OpenVinoImageTensorFactory.Create(messages);
                try
                {
                    var openVinoGeneration = openVinoSession.GenerateWithStats(
                        messages,
                        streamer: delta => onDelta?.Invoke(delta).GetAwaiter().GetResult(),
                        images: imageTensors.Length == 0 ? null : imageTensors,
                        cancellationToken: cancellationToken);
                    return new GenerationResult(openVinoGeneration.Text, openVinoGeneration.TokenCount, TimeSpan.Zero, openVinoGeneration.TokensPerSecond, openVinoGeneration.PromptTokenCount, "stop", null, openVinoGeneration.TimeToFirstTokenMs, openVinoGeneration.PrefillDurationMs, openVinoGeneration.DecodeDurationMs);
                }
                finally
                {
                    foreach (var imageTensor in imageTensors)
                        imageTensor.Dispose();
                }
            }

            if (string.Equals(backend, "vLLM", StringComparison.OrdinalIgnoreCase))
            {
                using var pythonSession = modelRuntime.CreatePythonChatSession();
                return await pythonSession.GenerateWithStatsAsync(messages, onDelta, cancellationToken).ConfigureAwait(false);
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
            markFatalBackendFailure(exception);
            throw;
        }
    }
}
