using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Esi.AI.Studio.Services;

/// <summary>Normalizes OpenAI chat requests and dispatches them through one backend contract.</summary>
public sealed class OpenAiCompatibleBackendMiddleware(
    ModelRuntime modelRuntime,
    IInferenceScheduler scheduler,
    IInferenceFailureCoordinator? failureCoordinator = null,
    InferenceTimeoutPolicy? timeoutPolicy = null,
    ILogger<OpenAiCompatibleBackendMiddleware>? logger = null,
    InferenceLogService? inferenceLogService = null)
{
    private const int MaximumImageBytes = 20 * 1024 * 1024;
    private const int MaximumOpenVinoTools = 32;
    private const int MaximumOpenVinoOutputTokens = 4096;
    private readonly IInferenceFailureCoordinator effectiveFailureCoordinator = failureCoordinator ?? NoOpInferenceFailureCoordinator.Instance;
    private readonly InferenceTimeoutPolicy effectiveTimeoutPolicy = timeoutPolicy ?? new();
    private readonly ILogger<OpenAiCompatibleBackendMiddleware> effectiveLogger = logger ?? NullLogger<OpenAiCompatibleBackendMiddleware>.Instance;

    /// <summary>Converts an OpenAI request into the shared backend-neutral request.</summary>
    public OpenAiBackendChatRequest Prepare(OpenAiChatRequest request, ModelLoadStatus status)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(status);
        if (request.Messages is null || request.Messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(request));

        var structuredMessages = request.Messages.ToArray();
        var messages = structuredMessages.Select(ParseMessage).ToArray();
        if (request.ResponseFormat is not null || messages.Select((message, index) => (message, index))
            .Any(item => string.IsNullOrWhiteSpace(item.message.Content) && item.message.Images is not { Count: > 0 } && !IsToolMessage(structuredMessages[item.index])))
            throw new ArgumentException("Every chat message requires non-empty text or image content.", nameof(request));

        ValidateImageCapability(status, messages);
        ValidateToolCapability(status, request.Tools);
        var tools = string.Equals(status.Backend, "OpenVINO", StringComparison.OrdinalIgnoreCase)
            ? LimitOpenVinoTools(request.Tools, request.ToolChoice)
            : request.Tools;
        if (status.Backend is "Vulkan" or "VULKAN" or "CUDA" or "SYCL" or "CPU" &&
            !string.IsNullOrWhiteSpace(status.ModelPath) &&
            !string.Equals(Path.GetExtension(status.ModelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("LLama chat requires a .gguf model path.", nameof(request));

        return new OpenAiBackendChatRequest(
            status.Backend ?? throw new InvalidOperationException("The loaded model backend is unavailable."),
            string.IsNullOrWhiteSpace(request.Model) ? GetModelId(status) : request.Model,
            status.ModelPath,
            structuredMessages,
            messages,
            tools,
            ToGenerationOptions(request, tools, string.Equals(status.Backend, "OpenVINO", StringComparison.OrdinalIgnoreCase)),
            BackendVariantId: status.BackendVariantId);
    }

    /// <summary>Runs a normalized request through the selected backend and preserves streaming semantics.</summary>
    /// <param name="request">The normalized backend request.</param>
    /// <param name="onDelta">The optional callback for generated text deltas.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <param name="inferenceId">The optional correlation ID assigned at the request boundary.</param>
    /// <param name="isStreaming">Whether the client requested a streamed response.</param>
    /// <returns>The generation result.</returns>
    public async Task<GenerationResult> GenerateAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default,
        string? inferenceId = null,
        bool isStreaming = false)
    {
        inferenceId = string.IsNullOrWhiteSpace(inferenceId) ? $"inf-{Guid.NewGuid():N}" : inferenceId;
        var imageCount = request.Messages.Sum(message => message.Images?.Count ?? 0);
        using var scope = effectiveLogger.BeginScope(new Dictionary<string, object?>
        {
            ["InferenceId"] = inferenceId,
            ["InferencePath"] = "OpenAI",
            ["Backend"] = request.Backend,
            ["Model"] = request.Model,
            ["BackendVariantId"] = request.BackendVariantId,
            ["MessageCount"] = request.Messages.Count,
            ["ImageCount"] = imageCount,
            ["ToolCount"] = request.Tools?.Count ?? 0,
            ["Streaming"] = isStreaming
        });
        var stopwatch = Stopwatch.StartNew();
        effectiveLogger.LogInformation(
            "Inference {InferenceId} started via OpenAI on backend {Backend}, model {Model}, variant {BackendVariantId}, {MessageCount} messages, {ImageCount} images, {ToolCount} tools; streaming={Streaming}.",
            inferenceId,
            request.Backend,
            request.Model,
            request.BackendVariantId,
            request.Messages.Count,
            imageCount,
            request.Tools?.Count ?? 0,
            isStreaming);

        InferenceLog? inferenceLog = null;
        Exception? fatalBackendFailure = null;
        GenerationResult result;
        try
        {
            if (inferenceLogService is not null)
            {
                inferenceLog = await inferenceLogService.InferenceLog_CreateAsync(
                    new InferenceLog(
                        Guid.NewGuid(),
                        inferenceId,
                        DateTimeOffset.UtcNow,
                        "OpenAI",
                        request.Backend,
                        request.Model,
                        request.BackendVariantId,
                        request.Messages.Count,
                        imageCount,
                        request.Tools?.Count ?? 0,
                        isStreaming,
                        InferenceLogStatus.Running),
                    cancellationToken).ConfigureAwait(false);
            }

            var decision = await effectiveTimeoutPolicy.CalculateAsync(request, cancellationToken).ConfigureAwait(false);
            if (inferenceLog is not null && inferenceLogService is not null)
            {
                inferenceLog = await inferenceLogService.InferenceLog_UpdateAsync(
                    inferenceLog with { EstimatedPrefillTokens = decision.EstimatedPrefillTokens },
                    cancellationToken).ConfigureAwait(false);
            }

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(decision.Timeout);

            try
            {
                result = await scheduler.RunAsync(
                    () => GenerateCoreAsync(request, onDelta, inferenceId, exception => fatalBackendFailure = exception, timeoutCancellation.Token),
                    timeoutCancellation.Token).ConfigureAwait(false);
                result = ApplyCompletionLimitFinishReason(result, request.Options.MaxTokens);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCancellation.IsCancellationRequested)
            {
                throw new InferenceTimeoutException(decision);
            }

        }
        catch (InferenceTimeoutException exception)
        {
            effectiveLogger.LogWarning(
                exception,
                "Inference {InferenceId} timed out after {ElapsedMilliseconds} ms.",
                inferenceId,
                stopwatch.ElapsedMilliseconds);
            if (inferenceLog is not null && inferenceLogService is not null)
            {
                await inferenceLogService.InferenceLog_UpdateAsync(
                    inferenceLog with
                    {
                        Status = InferenceLogStatus.TimedOut,
                        CompletedAtUtc = DateTimeOffset.UtcNow,
                        DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                        ErrorType = exception.GetType().FullName,
                        ErrorMessage = exception.Message
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }

            throw;
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

    internal static ChatMessage ParseMessage(OpenAiChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Content is null)
            return new ChatMessage(message.Role, string.Empty, ToolCalls: message.ToolCalls, ToolCallId: message.ToolCallId);
        if (message.Content is string text)
            return new ChatMessage(message.Role, text, ToolCalls: message.ToolCalls, ToolCallId: message.ToolCallId);
        if (message.Content is JsonElement { ValueKind: JsonValueKind.String } textElement)
            return new ChatMessage(message.Role, textElement.GetString() ?? string.Empty, ToolCalls: message.ToolCalls, ToolCallId: message.ToolCallId);
        if (message.Content is not JsonElement { ValueKind: JsonValueKind.Array } parts)
            throw new ArgumentException("Message content must be a string or an array of text and image parts.", nameof(message));

        var textBuilder = new StringBuilder();
        var images = new List<ChatImage>();
        var contentParts = new List<ChatMessageContentPart>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out var typeProperty) ||
                typeProperty.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Every message content part requires a type.", nameof(message));

            switch (typeProperty.GetString())
            {
                case "text":
                    if (!part.TryGetProperty("text", out var textProperty) || textProperty.ValueKind != JsonValueKind.String)
                        throw new ArgumentException("Text content parts require a text value.", nameof(message));
                    var textPart = textProperty.GetString() ?? string.Empty;
                    textBuilder.Append(textPart);
                    contentParts.Add(new ChatMessageContentPart(textPart));
                    break;
                case "image_url":
                    var imageIndex = images.Count;
                    images.Add(ParseImagePart(part));
                    contentParts.Add(new ChatMessageContentPart(ImageIndex: imageIndex));
                    break;
                default:
                    throw new ArgumentException("Only text and image_url content parts are supported by local backends.", nameof(message));
            }
        }

        return new ChatMessage(
            message.Role,
            textBuilder.ToString(),
            images.Count == 0 ? null : images,
            contentParts,
            message.ToolCalls,
            message.ToolCallId);
    }

    private async Task<GenerationResult> GenerateCoreAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onDelta,
        string inferenceId,
        Action<Exception> markFatalBackendFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.BackendVariantId) && modelRuntime.HasBackendRuntime(request.BackendVariantId))
                return await modelRuntime.GenerateBackendAsync(request, onDelta, cancellationToken).ConfigureAwait(false);

            return request.Backend switch
            {
                "OpenVINO" => await GenerateOpenVinoAsync(request, onDelta, cancellationToken).ConfigureAwait(false),
                "vLLM" => await GeneratePythonAsync(request, onDelta, cancellationToken).ConfigureAwait(false),
                "Vulkan" or "VULKAN" or "CUDA" or "SYCL" or "CPU" => await GenerateLlamaAsync(request, onDelta, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unsupported backend '{request.Backend}'.", nameof(request))
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
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

    private static GenerationResult ApplyCompletionLimitFinishReason(GenerationResult result, int maxTokens)
    {
        if (maxTokens <= 0 ||
            result.TokenCount < maxTokens ||
            result.FinishReason != "stop" ||
            result.ToolCalls is { Count: > 0 })
            return result;

        return result with { FinishReason = "length" };
    }

    private Task<GenerationResult> GenerateOpenVinoAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imageTensors = OpenVinoImageTensorFactory.Create(request.Messages);
            try
            {
                using var session = modelRuntime.CreateOpenVinoChatSession();
                Action<string> streamer = delta => onDelta?.Invoke(delta).GetAwaiter().GetResult();
                var result = session.GenerateWithStats(
                    request.StructuredMessages,
                    ResolveOpenVinoTools(request.Tools, request.Options.ToolChoice),
                    streamer,
                    ToOpenVinoOptions(request.Options),
                    imageTensors.Length == 0 ? null : imageTensors,
                    cancellationToken);
                return new GenerationResult(result.Text, result.TokenCount, TimeSpan.Zero, result.TokensPerSecond, result.PromptTokenCount, result.FinishReason, result.ToolCalls, result.TimeToFirstTokenMs, result.PrefillDurationMs, result.DecodeDurationMs);
            }
            finally
            {
                foreach (var imageTensor in imageTensors)
                    imageTensor.Dispose();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task<GenerationResult> GenerateLlamaAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreateLlamaChatSession("You are a helpful assistant.", request.ModelPath);
        return ParseToolResult(await session.GenerateWithStatsAsync(request.Messages, onDelta, request.Options, cancellationToken).ConfigureAwait(false));
    }

    private async Task<GenerationResult> GeneratePythonAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreatePythonChatSession();
        return ParseToolResult(await session.GenerateWithStatsAsync(request.Messages, onDelta, request.Options, cancellationToken).ConfigureAwait(false));
    }

    private static GenerationResult ParseToolResult(GenerationResult result)
    {
        var parsed = OpenAiToolCallParser.Parse(result.Text);
        return parsed.ToolCalls.Count == 0
            ? result
            : result with { Text = parsed.Text, ToolCalls = parsed.ToolCalls, FinishReason = "tool_calls" };
    }

    private void ValidateImageCapability(ModelLoadStatus status, IReadOnlyList<ChatMessage> messages)
    {
        if (!messages.Any(message => message.Images is { Count: > 0 }))
            return;
        if (status.Backend is not ("OpenVINO" or "Vulkan" or "VULKAN" or "CUDA" or "SYCL" or "CPU"))
            throw new ArgumentException($"Image input is not supported by the '{status.Backend}' backend.", nameof(messages));
        if (!modelRuntime.SupportsImageInput(status.Backend!, status.ModelPath))
            throw new ArgumentException("The loaded model does not support image input.", nameof(messages));
    }

    private static void ValidateToolCapability(ModelLoadStatus status, IReadOnlyList<OpenAiToolDefinition>? tools)
    {
        if (tools is not { Count: > 0 })
            return;
        if (status.Backend is "OpenVINO" or "vLLM")
            return;

        throw new ArgumentException(
            $"Structured tools are not supported by the '{status.Backend}' backend because its current runtime adapter has no native tool-template or tool-parser contract.",
            nameof(tools));
    }

    private static string GetModelId(ModelLoadStatus status) =>
        string.IsNullOrWhiteSpace(status.ModelPath) ? "local-model" : Path.GetFileNameWithoutExtension(status.ModelPath);

    private static ChatGenerationOptions ToGenerationOptions(
        OpenAiChatRequest request,
        IReadOnlyList<OpenAiToolDefinition>? tools,
        bool isOpenVino) => new(
        MaxTokens: isOpenVino
            ? Math.Min(request.MaxCompletionTokens ?? request.MaxTokens ?? 128, MaximumOpenVinoOutputTokens)
            : request.MaxCompletionTokens ?? request.MaxTokens ?? 128,
        Temperature: request.Temperature ?? .7f,
        TopP: request.TopP ?? .9f,
        TopK: request.TopK ?? 50,
        MinP: request.MinP ?? .1f,
        RepetitionPenalty: request.RepetitionPenalty ?? 1f,
        Seed: request.Seed,
        StopSequences: request.Stop,
        ReasoningEffort: request.ReasoningEffort,
        Tools: tools,
        ToolChoice: request.ToolChoice);

    private static IReadOnlyList<OpenAiToolDefinition>? LimitOpenVinoTools(
        IReadOnlyList<OpenAiToolDefinition>? tools,
        JsonElement? toolChoice)
    {
        if (tools is not { Count: > MaximumOpenVinoTools })
            return tools;

        var selectedToolName = GetSelectedToolName(toolChoice);
        var selectedTool = selectedToolName is null
            ? null
            : tools.FirstOrDefault(tool => string.Equals(tool.Function.Name, selectedToolName, StringComparison.Ordinal));
        var limitedTools = tools
            .Where(tool => !ReferenceEquals(tool, selectedTool))
            .Take(MaximumOpenVinoTools - (selectedTool is null ? 0 : 1))
            .ToList();
        if (selectedTool is not null)
            limitedTools.Add(selectedTool);

        return limitedTools;
    }

    private static string? GetSelectedToolName(JsonElement? toolChoice)
    {
        if (toolChoice is not JsonElement choice || choice.ValueKind != JsonValueKind.Object ||
            !choice.TryGetProperty("function", out var function) ||
            !function.TryGetProperty("name", out var name) ||
            name.ValueKind != JsonValueKind.String)
            return null;

        return name.GetString();
    }

    internal static OpenVinoGenerationOptions ToOpenVinoOptions(ChatGenerationOptions options) => new(
        MaxNewTokens: options.MaxTokens,
        Temperature: options.Temperature,
        TopP: options.TopP,
        DoSample: options.Temperature > 0,
        RepetitionPenalty: options.RepetitionPenalty,
        FrequencyPenalty: options.FrequencyPenalty,
        PresencePenalty: options.PresencePenalty,
        Seed: options.Seed,
        StopSequences: options.StopSequences,
        ReasoningEffort: options.ReasoningEffort);

    internal static IReadOnlyList<OpenAiToolDefinition>? ResolveOpenVinoTools(
        IReadOnlyList<OpenAiToolDefinition>? tools,
        JsonElement? toolChoice)
    {
        if (tools is not { Count: > 0 })
            return tools;
        if (toolChoice is not JsonElement choice || choice.ValueKind == JsonValueKind.Null || choice.ValueKind == JsonValueKind.Undefined)
            return tools;
        if (choice.ValueKind == JsonValueKind.String)
        {
            return choice.GetString() switch
            {
                "none" => null,
                "auto" or "required" => tools,
                _ => throw new ArgumentException($"Unsupported tool_choice value '{choice.GetString()}'.", nameof(toolChoice))
            };
        }

        if (choice.ValueKind != JsonValueKind.Object ||
            !choice.TryGetProperty("function", out var function) ||
            !function.TryGetProperty("name", out var name) ||
            name.ValueKind != JsonValueKind.String)
            throw new ArgumentException("tool_choice must be 'auto', 'required', 'none', or a function selection object.", nameof(toolChoice));

        var selectedTool = tools.FirstOrDefault(tool => string.Equals(tool.Function.Name, name.GetString(), StringComparison.Ordinal));
        return selectedTool is null
            ? throw new ArgumentException($"tool_choice references unknown function '{name.GetString()}'.", nameof(toolChoice))
            : [selectedTool];
    }

    private static bool IsToolMessage(OpenAiChatMessage message) =>
        string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase) || message.ToolCalls is { Count: > 0 };

    private static ChatImage ParseImagePart(JsonElement part)
    {
        if (!part.TryGetProperty("image_url", out var imageUrl) || imageUrl.ValueKind != JsonValueKind.Object ||
            !imageUrl.TryGetProperty("url", out var urlProperty) || urlProperty.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Image content parts require an image_url.url value.", nameof(part));

        var url = urlProperty.GetString();
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only local data image URLs are supported; remote image URLs are not fetched.", nameof(part));
        var comma = url.IndexOf(',');
        if (comma < 0)
            throw new ArgumentException("The image data URL is invalid.", nameof(part));
        var metadata = url["data:".Length..comma].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mediaType = metadata.FirstOrDefault() ?? string.Empty;
        if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            !metadata.Skip(1).Any(value => value.Equals("base64", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Images must use a base64 data URL with an image media type.", nameof(part));

        byte[] data;
        try
        {
            data = Convert.FromBase64String(url[(comma + 1)..]);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The image data URL contains invalid base64 data.", nameof(part), exception);
        }
        if (data.Length == 0 || data.Length > MaximumImageBytes)
            throw new ArgumentException("Image data must be between 1 byte and 20 MiB.", nameof(part));
        return new ChatImage(mediaType, data);
    }
}
