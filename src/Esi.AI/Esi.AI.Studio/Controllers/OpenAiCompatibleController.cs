using System.Text.Json;
using System.Text;
using System.Threading.Channels;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Controllers;

[ApiController]
[Route("v1")]
public sealed class OpenAiCompatibleController(
    ModelRuntime modelRuntime,
    ILocalModelCatalog localModelCatalog,
    IOmniRouteClient omniRouteClient,
    IOptions<OmniRouteOptions> omniRouteOptions,
    DataService? dataService = null) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SseHeartbeatInterval = TimeSpan.FromSeconds(15);

    [HttpGet("models")]
    public async Task<IActionResult> ListModels(CancellationToken cancellationToken)
    {
        if (omniRouteOptions.Value.Enabled)
        {
            var upstreamModels = await omniRouteClient.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            if (upstreamModels.Succeeded)
                return Ok(upstreamModels.Models);

            return StatusCode(upstreamModels.StatusCode, CreateError("OmniRoute model discovery failed.", "upstream_error"));
        }

        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var loadedModels = modelRuntime.LoadedModel_Read().LoadedModels
            .Where(loadedModel => !loadedModel.IsLoading)
            .ToArray();
        bool IsLoaded(string modelPath) => loadedModels.Any(loadedModel =>
            string.Equals(modelPath, loadedModel.ModelPath, StringComparison.OrdinalIgnoreCase));
        ModelCapabilities GetCapabilities(string modelPath, ModelCapabilities? storedCapabilities)
        {
            var capabilities = storedCapabilities ?? new ModelCapabilities();
            var loadedModel = loadedModels.FirstOrDefault(loaded =>
                string.Equals(modelPath, loaded.ModelPath, StringComparison.OrdinalIgnoreCase));
            return loadedModel is not null &&
                modelRuntime.SupportsImageInput(loadedModel.Runtime, loadedModel.ModelPath)
                ? capabilities with { ImageInput = true }
                : capabilities;
        }
        var apiModels = dataService is null
            ? (await localModelCatalog.ScanLocalModelsAsync(cancellationToken).ConfigureAwait(false))
                .Select(model => new OpenAiModel(model.Path, "model", created, "esi-ai", model.Name, GetCapabilities(model.Path, null), IsLoaded(model.Path)))
                .ToList()
            : (await dataService.LocalModel_ReadAsync(cancellationToken).ConfigureAwait(false))
                .Select(model => new OpenAiModel(model.Path, "model", created, "esi-ai", model.Name, GetCapabilities(model.Path, model.Capabilities), IsLoaded(model.Path)))
                .ToList();
        foreach (var loadedModel in loadedModels.Where(loadedModel => apiModels.All(model => !string.Equals(model.Id, loadedModel.ModelPath, StringComparison.OrdinalIgnoreCase))))
            apiModels.Add(new OpenAiModel(
                loadedModel.ModelPath,
                "model",
                created,
                "esi-ai",
                Path.GetFileName(loadedModel.ModelPath),
                new ModelCapabilities(ImageInput: modelRuntime.SupportsImageInput(loadedModel.Runtime, loadedModel.ModelPath)),
                Loaded: true));

        return Ok(new OpenAiModelListResponse("list", apiModels));
    }

    /// <summary>Returns the currently loaded and loading application models.</summary>
    [HttpGet("application/models")]
    public IActionResult GetApplicationModels() => Ok(modelRuntime.LoadedModel_Read());

    /// <summary>Returns internal model IDs and their persisted backend configurations.</summary>
    [HttpGet("application/models/catalog")]
    public async Task<IActionResult> GetApplicationModelCatalog(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await RequireDataService().ApplicationModelCatalog_ReadAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    /// <summary>Loads an internal model using one of its persisted configurations.</summary>
    [HttpPost("application/models/load")]
    public Task<IActionResult> LoadConfiguredModel(
        ApplicationModelLoadRequest? request,
        CancellationToken cancellationToken) =>
        ExecuteApplicationModelOperationAsync(
            request,
            modelRequest => RequireDataService().LoadConfiguredModelAsync(modelRequest, cancellationToken),
            cancellationToken,
            requestValidator: modelRequest => modelRequest.ModelId == Guid.Empty
                ? CreateError("ModelId is required.", "invalid_request_error")
                : modelRequest.ConfigurationId == Guid.Empty
                    ? CreateError("ConfigurationId is required.", "invalid_request_error")
                    : null);

    /// <summary>Loads a Llama model through the application API.</summary>
    [HttpPost("application/models/load/llama")]
    public Task<IActionResult> LoadLlamaModel(
        LoadModelRequest? request,
        CancellationToken cancellationToken) =>
        ExecuteApplicationModelOperationAsync(
            request,
            modelRequest => RequireDataService().LoadModelAsync(modelRequest, cancellationToken),
            cancellationToken);

    /// <summary>Loads an OpenVINO model through the application API.</summary>
    [HttpPost("application/models/load/openvino")]
    public Task<IActionResult> LoadOpenVinoModel(
        OpenVinoLoadRequest? request,
        CancellationToken cancellationToken) =>
        ExecuteApplicationModelOperationAsync(
            request,
            modelRequest => RequireDataService().LoadModelAsync(modelRequest, cancellationToken),
            cancellationToken);

    /// <summary>Loads a vLLM or SGLang model through the application API.</summary>
    [HttpPost("application/models/load/python")]
    public Task<IActionResult> LoadPythonModel(
        PythonInferenceLoadRequest? request,
        CancellationToken cancellationToken) =>
        ExecuteApplicationModelOperationAsync(
            request,
            modelRequest => RequireDataService().LoadPythonModelAsync(modelRequest, cancellationToken),
            cancellationToken);

    /// <summary>Loads a dotLLM model through the application API.</summary>
    [HttpPost("application/models/load/dotllm")]
    public Task<IActionResult> LoadDotLlmModel(
        DotLlmLoadRequest? request,
        CancellationToken cancellationToken) =>
        ExecuteApplicationModelOperationAsync(
            request,
            modelRequest => RequireDataService().LoadDotLlmModelAsync(modelRequest, cancellationToken),
            cancellationToken);

    /// <summary>Unloads one model selected by path and backend through the application API.</summary>
    [HttpPost("application/models/unload")]
    public Task<IActionResult> UnloadApplicationModel(
        ApplicationModelUnloadRequest? request,
        CancellationToken cancellationToken) =>
        ExecuteApplicationModelOperationAsync(
            request,
            modelRequest => RequireDataService().UnloadModelAsync(modelRequest.ModelPath, modelRequest.Backend, cancellationToken),
            cancellationToken,
            requestValidator: modelRequest => string.IsNullOrWhiteSpace(modelRequest.ModelPath)
                ? CreateError("ModelPath is required.", "invalid_request_error")
                : null);

    [HttpPost("chat/completions")]
    public async Task<IActionResult> CreateChatCompletion(
        OpenAiChatRequest? request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request, omniRouteOptions.Value.Enabled);
        if (validationError is not null)
            return BadRequest(validationError);

        try
        {
            if (omniRouteOptions.Value.Enabled)
                return await ForwardToOmniRouteAsync(request!, cancellationToken).ConfigureAwait(false);

            var status = GetLoadedModelStatus(request!.Model);
            var messages = request.Messages!.Select(ParseMessage).ToArray();
            var imageCapabilityError = ValidateImageCapability(status, messages);
            if (imageCapabilityError is not null)
                return BadRequest(imageCapabilityError);
            var hasEmptyMessage = messages.Select((message, index) => (message, index))
                .Any(item => string.IsNullOrWhiteSpace(item.message.Content) && item.message.Images is not { Count: > 0 } && !IsToolMessage(request.Messages![item.index]));
            if (request.ResponseFormat is not null || hasEmptyMessage)
                return BadRequest(CreateError("Every chat message requires non-empty text or image content.", "invalid_request_error"));
            var model = string.IsNullOrWhiteSpace(request.Model) ? GetModelId(status) : request.Model;
            var options = ToGenerationOptions(request);

            if (request.Stream)
            {
                await StreamCompletionAsync(
                    status.Backend!,
                    status.ModelPath,
                    messages,
                    model,
                    options,
                    request.Messages,
                    request.Tools,
                    request.StreamOptions?.IncludeUsage == true,
                    cancellationToken).ConfigureAwait(false);
                return new EmptyResult();
            }

            var result = await GenerateAsync(
                status.Backend!,
                status.ModelPath,
                messages,
                null,
                options,
                cancellationToken,
                request.Messages,
                request.Tools).ConfigureAwait(false);
            return Ok(CreateCompletion(result, model, result.FinishReason));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, CreateError("OmniRoute request timed out.", "upstream_error"));
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, CreateError("OmniRoute is unavailable.", "upstream_error"));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(CreateError(exception.Message, "invalid_request_error"));
        }
        catch (InvalidOperationException exception)
        {
            if (Response.HasStarted)
                await WriteSseErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
            else
                return StatusCode(StatusCodes.Status503ServiceUnavailable, CreateError(exception.Message, "server_error"));

            return new EmptyResult();
        }
        catch (Exception exception) when (Response.HasStarted)
        {
            await WriteSseErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
            return new EmptyResult();
        }
    }

    private async Task<IActionResult> ForwardToOmniRouteAsync(OpenAiChatRequest request, CancellationToken cancellationToken)
    {
        using var upstreamResponse = await omniRouteClient.CreateChatCompletionAsync(
            request,
            Request.Headers.Authorization.ToString(),
            cancellationToken).ConfigureAwait(false);

        if (!upstreamResponse.IsSuccessStatusCode)
            return StatusCode((int)upstreamResponse.StatusCode, CreateError("OmniRoute chat completion failed.", "upstream_error"));

        Response.StatusCode = (int)upstreamResponse.StatusCode;
        if (upstreamResponse.Content.Headers.ContentType is not null)
            Response.ContentType = upstreamResponse.Content.Headers.ContentType.ToString();
        if (upstreamResponse.Headers.CacheControl is not null)
            Response.Headers.CacheControl = upstreamResponse.Headers.CacheControl.ToString();
        if (request.Stream)
            Response.Headers["X-Accel-Buffering"] = "no";

        await upstreamResponse.Content.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
        return new EmptyResult();
    }

    private async Task StreamCompletionAsync(
        string backend,
        string? modelPath,
        IReadOnlyList<ChatMessage> messages,
        string model,
        ChatGenerationOptions options,
        IReadOnlyList<OpenAiChatMessage>? openAiMessages,
        IReadOnlyList<OpenAiToolDefinition>? tools,
        bool includeUsage,
        CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");

        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        await WriteSseAsync(CreateChunk(completionId, model, new OpenAiChatCompletionDelta("assistant"), null), cancellationToken).ConfigureAwait(false);

        var structuredOpenVinoOutput = string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase) && tools is { Count: > 0 };
        var deltas = Channel.CreateUnbounded<string>();
        var generationTask = GenerateAsync(
            backend,
            modelPath,
            messages,
            structuredOpenVinoOutput
                ? null
                : delta =>
                {
                    deltas.Writer.TryWrite(delta);
                    return Task.CompletedTask;
                },
            options,
            cancellationToken,
            openAiMessages,
            tools);
        _ = CompleteChannelAsync(generationTask, deltas.Writer);

        try
        {
            var readTask = deltas.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var heartbeatTask = Task.Delay(SseHeartbeatInterval, cancellationToken);
            while (true)
            {
                var completedTask = await Task.WhenAny(readTask, heartbeatTask).ConfigureAwait(false);
                if (completedTask == heartbeatTask)
                {
                    await WriteSseHeartbeatAsync(cancellationToken).ConfigureAwait(false);
                    heartbeatTask = Task.Delay(SseHeartbeatInterval, cancellationToken);
                    continue;
                }

                if (!await readTask.ConfigureAwait(false))
                    break;

                while (deltas.Reader.TryRead(out var delta))
                {
                    if (!string.IsNullOrEmpty(delta))
                        await WriteSseAsync(CreateChunk(completionId, model, new OpenAiChatCompletionDelta(Content: delta), null), cancellationToken).ConfigureAwait(false);
                }

                readTask = deltas.Reader.WaitToReadAsync(cancellationToken).AsTask();
            }
            await generationTask.ConfigureAwait(false);
        }
        catch (ChannelClosedException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }

        var generationResult = await generationTask.ConfigureAwait(false);
        var finalDelta = structuredOpenVinoOutput
            ? new OpenAiChatCompletionDelta(
                Content: string.IsNullOrEmpty(generationResult.Text) ? null : generationResult.Text,
                ToolCalls: ToToolCallDeltas(generationResult.ToolCalls))
            : new OpenAiChatCompletionDelta();
        await WriteSseAsync(CreateChunk(
            completionId,
            model,
            finalDelta,
            generationResult.FinishReason,
            includeUsage ? CreateUsage(generationResult) : null), cancellationToken).ConfigureAwait(false);
        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<GenerationResult> GenerateAsync(
        string backend,
        string? modelPath,
        IReadOnlyList<ChatMessage> messages,
        Func<string, Task>? onDelta,
        ChatGenerationOptions options,
        CancellationToken cancellationToken,
        IReadOnlyList<OpenAiChatMessage>? openAiMessages = null,
        IReadOnlyList<OpenAiToolDefinition>? tools = null) =>
        backend switch
        {
            "OpenVINO" => StartOpenVinoGeneration(messages, openAiMessages ?? messages.Select(message => new OpenAiChatMessage(message.Role, message.Content)).ToArray(), tools, onDelta, options, cancellationToken),
            "vLLM" or "SGLang" => GeneratePythonAsync(messages, onDelta, options, cancellationToken),
            "dotLLM" => GenerateDotLlmAsync(messages, onDelta, options, cancellationToken),
            "Vulkan" or "VULKAN" or "CUDA" or "SYCL" or "CPU" => GenerateLlamaAsync(modelPath, messages, onDelta, options, cancellationToken),
            _ => throw new ArgumentException($"Unsupported model backend '{backend}'.", nameof(backend))
        };

    private Task<GenerationResult> StartOpenVinoGeneration(
        IReadOnlyList<ChatMessage> chatMessages,
        IReadOnlyList<OpenAiChatMessage> openAiMessages,
        IReadOnlyList<OpenAiToolDefinition>? tools,
        Func<string, Task>? onDelta,
        ChatGenerationOptions options,
        CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imageTensors = OpenVinoImageTensorFactory.Create(chatMessages);
            try
            {
                using var session = modelRuntime.CreateOpenVinoChatSession();
                Action<string>? streamer = onDelta is null
                    ? null
                    : delta =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        onDelta(delta).GetAwaiter().GetResult();
                    };
                var result = session.GenerateWithStats(
                    openAiMessages,
                    tools,
                    streamer,
                    ToOpenVinoOptions(options),
                    imageTensors.Length == 0 ? null : imageTensors);
                return new GenerationResult(result.Text, result.TokenCount, TimeSpan.Zero, result.TokensPerSecond, result.PromptTokenCount, result.FinishReason, result.ToolCalls);
            }
            finally
            {
                foreach (var imageTensor in imageTensors)
                    imageTensor.Dispose();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task<GenerationResult> GenerateLlamaAsync(string? modelPath, IReadOnlyList<ChatMessage> messages, Func<string, Task>? onDelta, ChatGenerationOptions options, CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreateLlamaChatSession(string.Empty, modelPath);
        return await session.GenerateWithStatsAsync(messages, onDelta, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GenerationResult> GeneratePythonAsync(IReadOnlyList<ChatMessage> messages, Func<string, Task>? onDelta, ChatGenerationOptions options, CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreatePythonChatSession();
        return await session.GenerateWithStatsAsync(messages, onDelta, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GenerationResult> GenerateDotLlmAsync(IReadOnlyList<ChatMessage> messages, Func<string, Task>? onDelta, ChatGenerationOptions options, CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreateDotLlmChatSession();
        return await session.GenerateWithStatsAsync(messages, onDelta, options, cancellationToken).ConfigureAwait(false);
    }

    private ModelLoadStatus GetLoadedModelStatus(string? requestedModel)
    {
        var status = modelRuntime.LoadedModel_Read();
        if (!status.IsModelLoaded || string.IsNullOrWhiteSpace(status.Backend))
            throw new InvalidOperationException("No model is currently loaded.");

        if (string.IsNullOrWhiteSpace(requestedModel))
            return status;

        var loadedModel = status.LoadedModels.FirstOrDefault(model =>
            string.Equals(model.ModelPath, requestedModel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(model.ModelPath), requestedModel, StringComparison.OrdinalIgnoreCase));
        if (loadedModel is null)
            throw new InvalidOperationException($"The selected model '{requestedModel}' is not loaded. Load it in Esi.AI Studio first.");

        var backend = loadedModel.Backend switch
        {
            ConfigurationBackend.Llama => loadedModel.Runtime,
            ConfigurationBackend.OpenVino => "OpenVINO",
            ConfigurationBackend.Vllm => "vLLM",
            ConfigurationBackend.Sglang => "SGLang",
            ConfigurationBackend.DotLlm => "dotLLM",
            _ => status.Backend
        };
        return status with
        {
            ModelPath = loadedModel.ModelPath,
            Backend = backend,
            GpuLayerCount = loadedModel.GpuLayerCount,
            ContextSize = loadedModel.ContextSize,
            ModelSizeInBytes = loadedModel.ModelSizeInBytes,
            VulkanDevices = loadedModel.VulkanDevices,
            CpuModelBufferMiB = loadedModel.CpuModelBufferMiB,
            LoadLog = loadedModel.LoadLog,
            IsModelLoaded = !loadedModel.IsLoading
        };
    }

    private static OpenAiErrorResponse? ValidateRequest(OpenAiChatRequest? request, bool allowToolCalls)
    {
        if (request?.Messages is null || request.Messages.Count == 0)
            return CreateError("At least one chat message is required.", "invalid_request_error");
        if (request.Messages.Any(message => message is null || string.IsNullOrWhiteSpace(message.Role)))
            return CreateError("Every chat message requires a non-empty role.", "invalid_request_error");
        if (request.MaxTokens is <= 0 || request.MaxCompletionTokens is <= 0)
            return CreateError("max_tokens must be greater than zero.", "invalid_request_error");
        if (request.Temperature is < 0 or > 2 || request.TopP is <= 0 or > 1)
            return CreateError("temperature must be between 0 and 2 and top_p must be greater than 0 and at most 1.", "invalid_request_error");
        if (!allowToolCalls && (request.FrequencyPenalty is not null || request.PresencePenalty is not null))
            return CreateError("frequency_penalty and presence_penalty are not supported by local backends.", "unsupported_request_error");
        if (request.TopK is <= 0 || request.MinP is < 0 or > 1 || request.RepetitionPenalty is <= 0)
            return CreateError("top_k must be greater than zero, min_p must be between 0 and 1, and repetition_penalty must be greater than zero.", "invalid_request_error");
        if (request.ReasoningEffort is not null && !IsSupportedReasoningEffort(request.ReasoningEffort))
            return CreateError("reasoning_effort must be one of none, low, medium, high, xhigh, or max.", "invalid_request_error");
        return null;
    }

    private static ChatGenerationOptions ToGenerationOptions(OpenAiChatRequest request) => new(
        MaxTokens: request.MaxCompletionTokens ?? request.MaxTokens ?? 128,
        Temperature: request.Temperature ?? .7f,
        TopP: request.TopP ?? .9f,
        TopK: request.TopK ?? 50,
        MinP: request.MinP ?? .1f,
        RepetitionPenalty: request.RepetitionPenalty ?? 1f,
        Seed: request.Seed,
        StopSequences: request.Stop,
        ReasoningEffort: request.ReasoningEffort);

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

    private static bool IsSupportedReasoningEffort(string value) => value.Trim().ToLowerInvariant() switch
    {
        "none" or "low" or "medium" or "high" or "xhigh" or "max" => true,
        _ => false
    };

    private static OpenAiToolCallDelta[] ToToolCallDeltas(IReadOnlyList<OpenAiToolCall>? toolCalls) =>
        toolCalls is null
            ? []
            : toolCalls.Select((toolCall, index) => new OpenAiToolCallDelta(
                index,
                toolCall.Id,
                toolCall.Type,
                new OpenAiToolCallFunctionDelta(toolCall.Function.Name, toolCall.Function.Arguments))).ToArray();

    private static bool IsToolMessage(OpenAiChatMessage message) =>
        string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase) || message.ToolCalls is { Count: > 0 };

    private static string GetModelId(ModelLoadStatus status) =>
        string.IsNullOrWhiteSpace(status.ModelPath) ? "local-model" : Path.GetFileNameWithoutExtension(status.ModelPath);

    private static OpenAiChatCompletionResponse CreateCompletion(GenerationResult result, string model, string finishReason) =>
        new($"chatcmpl-{Guid.NewGuid():N}", "chat.completion", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
            new[] { new OpenAiChatCompletionChoice(
                0,
                new OpenAiChatMessage("assistant", string.IsNullOrEmpty(result.Text) ? null : result.Text, result.ToolCalls),
                finishReason) },
            CreateUsage(result));

    private static OpenAiUsage CreateUsage(GenerationResult result)
    {
        int? totalTokens = result.PromptTokenCount is int promptTokens
            ? promptTokens + result.TokenCount
            : null;
        return new OpenAiUsage(result.PromptTokenCount, result.TokenCount, totalTokens, result.TokensPerSecond);
    }

    internal static ChatMessage ParseMessage(OpenAiChatMessage message)
    {
        if (message.Content is null)
            return new ChatMessage(message.Role, string.Empty);
        if (message.Content is string text)
            return new ChatMessage(message.Role, text);
        if (message.Content is JsonElement { ValueKind: JsonValueKind.String } textElement)
            return new ChatMessage(message.Role, textElement.GetString() ?? string.Empty);
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
            contentParts);
    }

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

        const int maximumImageBytes = 20 * 1024 * 1024;
        if (data.Length == 0 || data.Length > maximumImageBytes)
            throw new ArgumentException("Image data must be between 1 byte and 20 MiB.", nameof(part));

        return new ChatImage(mediaType, data);
    }

    private OpenAiErrorResponse? ValidateImageCapability(ModelLoadStatus status, IReadOnlyList<ChatMessage> messages)
    {
        if (!messages.Any(message => message.Images is { Count: > 0 }))
            return null;

        if (status.Backend is not ("OpenVINO" or "Vulkan" or "VULKAN" or "CUDA" or "SYCL" or "CPU"))
            return CreateError($"Image input is not supported by the '{status.Backend}' backend.", "unsupported_request_error");
        if (!modelRuntime.SupportsImageInput(status.Backend, status.ModelPath))
            return CreateError("The loaded model does not support image input.", "unsupported_request_error");

        return null;
    }

    private static OpenAiChatCompletionChunk CreateChunk(
        string id,
        string model,
        OpenAiChatCompletionDelta delta,
        string? finishReason,
        OpenAiUsage? usage = null) =>
        new(id, "chat.completion.chunk", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
            new[] { new OpenAiChatCompletionChunkChoice(0, delta, finishReason) }, usage);

        private async Task<IActionResult> ExecuteApplicationModelOperationAsync<TRequest, TResponse>(
            TRequest? request,
            Func<TRequest, Task<TResponse>> operation,
            CancellationToken cancellationToken,
            Func<TRequest, OpenAiErrorResponse?>? requestValidator = null)
            where TRequest : class
        {
            if (HttpContext.Connection.RemoteIpAddress is { } remoteAddress && !System.Net.IPAddress.IsLoopback(remoteAddress))
                return StatusCode(StatusCodes.Status403Forbidden, CreateError("Application model operations are only available from the local machine.", "forbidden"));
            if (request is null)
                return BadRequest(CreateError("A request body is required.", "invalid_request_error"));

            var validationError = requestValidator?.Invoke(request);
            if (validationError is not null)
                return BadRequest(validationError);

            try
            {
                return Ok(await operation(request).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new EmptyResult();
            }
            catch (ArgumentException exception)
            {
                return BadRequest(CreateError(exception.Message, "invalid_request_error"));
            }
            catch (FileNotFoundException exception)
            {
                return BadRequest(CreateError(exception.Message, "invalid_request_error"));
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(CreateError(exception.Message, "not_found_error"));
            }
            catch (DirectoryNotFoundException exception)
            {
                return BadRequest(CreateError(exception.Message, "invalid_request_error"));
            }
            catch (InvalidOperationException exception)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, CreateError(exception.Message, "server_error"));
            }
        }

        private DataService RequireDataService() =>
            dataService ?? throw new InvalidOperationException("Application model operations are unavailable.");

    private static OpenAiErrorResponse CreateError(string message, string type) => new(new OpenAiError(message, type));

    private async Task WriteSseAsync(OpenAiChatCompletionChunk chunk, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk, SseJsonOptions)}\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSseErrorAsync(string message, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(CreateError(message, "server_error"), SseJsonOptions)}\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSseHeartbeatAsync(CancellationToken cancellationToken)
    {
        await Response.WriteAsync(": keep-alive\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CompleteChannelAsync(Task<GenerationResult> generationTask, ChannelWriter<string> writer)
    {
        try
        {
            await generationTask.ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }
}
