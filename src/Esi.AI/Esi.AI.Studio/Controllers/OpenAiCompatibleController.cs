using System.Text.Json;
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
        var apiModels = dataService is null
            ? (await localModelCatalog.ScanLocalModelsAsync(cancellationToken).ConfigureAwait(false))
                .Select(model => new OpenAiModel(model.Path, "model", created, "esi-ai", model.Name, Loaded: IsLoaded(model.Path)))
                .ToList()
            : (await dataService.LocalModel_ReadAsync(cancellationToken).ConfigureAwait(false))
                .Select(model => new OpenAiModel(model.Path, "model", created, "esi-ai", model.Name, model.Capabilities ?? new ModelCapabilities(), IsLoaded(model.Path)))
                .ToList();
        foreach (var loadedModel in loadedModels.Where(loadedModel => apiModels.All(model => !string.Equals(model.Id, loadedModel.ModelPath, StringComparison.OrdinalIgnoreCase))))
            apiModels.Add(new OpenAiModel(loadedModel.ModelPath, "model", created, "esi-ai", Path.GetFileName(loadedModel.ModelPath), Loaded: true));

        return Ok(new OpenAiModelListResponse("list", apiModels));
    }

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
            var messages = request!.Messages!.Select(message => new ChatMessage(message.Role, GetTextContent(message.Content))).ToArray();
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
            await foreach (var delta in deltas.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(delta))
                    await WriteSseAsync(CreateChunk(completionId, model, new OpenAiChatCompletionDelta(Content: delta), null), cancellationToken).ConfigureAwait(false);
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
            "OpenVINO" => StartOpenVinoGeneration(openAiMessages ?? messages.Select(message => new OpenAiChatMessage(message.Role, message.Content)).ToArray(), tools, onDelta, options, cancellationToken),
            "vLLM" or "SGLang" => GeneratePythonAsync(messages, onDelta, options, cancellationToken),
            "dotLLM" => GenerateDotLlmAsync(messages, onDelta, options, cancellationToken),
            "Vulkan" or "VULKAN" or "CPU" => GenerateLlamaAsync(modelPath, messages, onDelta, options, cancellationToken),
            _ => throw new ArgumentException($"Unsupported model backend '{backend}'.", nameof(backend))
        };

    private Task<GenerationResult> StartOpenVinoGeneration(
        IReadOnlyList<OpenAiChatMessage> messages,
        IReadOnlyList<OpenAiToolDefinition>? tools,
        Func<string, Task>? onDelta,
        ChatGenerationOptions options,
        CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var session = modelRuntime.CreateOpenVinoChatSession();
            Action<string>? streamer = onDelta is null
                ? null
                : delta =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    onDelta(delta).GetAwaiter().GetResult();
                };
            var result = session.GenerateWithStats(messages, tools, streamer, ToOpenVinoOptions(options));
            return new GenerationResult(result.Text, result.TokenCount, TimeSpan.Zero, result.TokensPerSecond, result.PromptTokenCount, result.FinishReason, result.ToolCalls);
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
        if (!allowToolCalls && request.Messages.Any(message => !IsTextContent(message.Content) && !IsToolMessage(message)))
            return CreateError("Only string message content is supported by local backends.", "invalid_request_error");
        if (!allowToolCalls && (request.ResponseFormat is not null || request.Messages.Any(message => string.IsNullOrWhiteSpace(GetTextContent(message.Content)) && !IsToolMessage(message))))
            return CreateError("Every chat message requires a non-empty role and content.", "invalid_request_error");
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

    private static string GetTextContent(object? content) => content switch
    {
        null => string.Empty,
        string text => text,
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
        _ => throw new ArgumentException("Only string message content is supported by local backends.", nameof(content))
    };

    private static bool IsTextContent(object? content) => content is null or string ||
        content is JsonElement element && element.ValueKind == JsonValueKind.String;

    private static OpenAiChatCompletionChunk CreateChunk(
        string id,
        string model,
        OpenAiChatCompletionDelta delta,
        string? finishReason,
        OpenAiUsage? usage = null) =>
        new(id, "chat.completion.chunk", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
            new[] { new OpenAiChatCompletionChunkChoice(0, delta, finishReason) }, usage);

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
