using System.Text;
using System.Text.Json;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Normalizes OpenAI chat requests and dispatches them through one backend contract.</summary>
public sealed class OpenAiCompatibleBackendMiddleware(
    ModelRuntime modelRuntime,
    IInferenceScheduler scheduler,
    IInferenceFailureCoordinator? failureCoordinator = null,
    InferenceTimeoutPolicy? timeoutPolicy = null)
{
    private const int MaximumImageBytes = 20 * 1024 * 1024;
    private const int MaximumOpenVinoTools = 32;
    private const int MaximumOpenVinoOutputTokens = 4096;
    private readonly IInferenceFailureCoordinator effectiveFailureCoordinator = failureCoordinator ?? NoOpInferenceFailureCoordinator.Instance;
    private readonly InferenceTimeoutPolicy effectiveTimeoutPolicy = timeoutPolicy ?? new();

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
            ToGenerationOptions(request, tools, string.Equals(status.Backend, "OpenVINO", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Runs a normalized request through the selected backend and preserves streaming semantics.</summary>
    public async Task<GenerationResult> GenerateAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default)
    {
        var decision = await effectiveTimeoutPolicy.CalculateAsync(request, cancellationToken).ConfigureAwait(false);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(decision.Timeout);

        try
        {
            return await scheduler.RunAsync(
                () => GenerateCoreAsync(request, onDelta, timeoutCancellation.Token),
                timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCancellation.IsCancellationRequested)
        {
            throw new InferenceTimeoutException(decision);
        }
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
        CancellationToken cancellationToken)
    {
        try
        {
            return request.Backend switch
            {
                "OpenVINO" => await GenerateOpenVinoAsync(request, onDelta, cancellationToken).ConfigureAwait(false),
                "vLLM" or "SGLang" => await GeneratePythonAsync(request, onDelta, cancellationToken).ConfigureAwait(false),
                "dotLLM" => await GenerateDotLlmAsync(request, onDelta, cancellationToken).ConfigureAwait(false),
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
            await effectiveFailureCoordinator.FailAsync(exception).ConfigureAwait(false);
            throw;
        }
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
                Action<string> streamer = delta =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        onDelta?.Invoke(delta).GetAwaiter().GetResult();
                    };
                var result = session.GenerateWithStats(
                    request.StructuredMessages,
                    ResolveOpenVinoTools(request.Tools, request.Options.ToolChoice),
                    streamer,
                    ToOpenVinoOptions(request.Options),
                    imageTensors.Length == 0 ? null : imageTensors);
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

    private async Task<GenerationResult> GenerateDotLlmAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreateDotLlmChatSession();
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
        if (status.Backend is "OpenVINO" or "vLLM" or "SGLang" or "dotLLM")
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
