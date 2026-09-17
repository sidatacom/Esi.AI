using System.Text.Json;
using System.Threading.Channels;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.AspNetCore.Mvc;

namespace Esi.AI.Studio.Controllers;

[ApiController]
[Route("v1")]
public sealed class OpenAiCompatibleController(
    ModelRuntime modelRuntime,
    ILocalModelCatalog localModelCatalog,
    OpenAiCompatibleBackendMiddleware backendMiddleware,
    DataService? dataService = null,
    ProviderTraceStore? providerTraceStore = null) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SseHeartbeatInterval = TimeSpan.FromSeconds(15);

    [HttpGet("models")]
    public async Task<IActionResult> ListModels(CancellationToken cancellationToken)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (dataService is not null)
        {
            var configurations = await dataService.ModelConfiguration_ReadAsync(cancellationToken).ConfigureAwait(false);
            var localModels = await dataService.LocalModel_ReadAsync(cancellationToken).ConfigureAwait(false);
            var loadedConfigurationModels = modelRuntime.LoadedModel_Read().LoadedModels
                .Where(loadedModel => !loadedModel.IsLoading)
                .ToArray();
            var apiConfigurations = configurations.Select(configuration =>
            {
                var localModel = localModels.FirstOrDefault(model =>
                    string.Equals(model.Path, configuration.ModelPath, StringComparison.OrdinalIgnoreCase));
                var loadedModel = loadedConfigurationModels.FirstOrDefault(model =>
                    string.Equals(model.ModelPath, configuration.ModelPath, StringComparison.OrdinalIgnoreCase) &&
                    model.Backend == configuration.Backend);
                var capabilities = localModel?.Capabilities ?? new ModelCapabilities();
                if (loadedModel is not null && modelRuntime.SupportsImageInput(loadedModel.Runtime, loadedModel.ModelPath))
                    capabilities = capabilities with { ImageInput = true };

                return new OpenAiModel(
                    configuration.Id.ToString("D"),
                    "model",
                    created,
                    "esi-ai",
                    configuration.Name,
                    capabilities,
                    loadedModel is not null,
                    configuration.Id,
                    configuration.Backend,
                    configuration.AutoLaunch);
            }).ToArray();

            return Ok(new OpenAiModelListResponse("list", apiConfigurations));
        }

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
        var requestId = $"req-{Guid.NewGuid():N}";
        await TraceAsync(
            requestId,
            "API",
            "in",
            "Request empfangen",
            "POST /v1/chat/completions",
            SerializeTracePayload(request)).ConfigureAwait(false);
        var validationError = ValidateRequest(request);
        if (validationError is not null)
            return BadRequest(validationError);

        try
        {
            if (request!.Stream)
            {
                await StreamCompletionAsync(
                    requestId,
                    request!,
                    request.StreamOptions?.IncludeUsage == true,
                    cancellationToken).ConfigureAwait(false);
                await TraceAsync(requestId, "API", "out", "Streaming-Antwort abgeschlossen", "SSE-Stream mit [DONE] beendet").ConfigureAwait(false);
                return new EmptyResult();
            }

            var backendRequest = await PrepareBackendRequestAsync(request!, requestId, null, cancellationToken).ConfigureAwait(false);
            var result = await backendMiddleware.GenerateAsync(backendRequest, null, cancellationToken).ConfigureAwait(false);
            await TraceGenerationResultAsync(requestId, result).ConfigureAwait(false);
            await TraceAsync(requestId, "API", "out", "Antwort an Client", $"HTTP 200 · finish_reason={result.FinishReason}").ConfigureAwait(false);
            return Ok(CreateCompletion(result, backendRequest.Model, result.FinishReason));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TraceAsync(requestId, "API", "error", "Request abgebrochen", "Die Client-Verbindung wurde beendet").ConfigureAwait(false);
            return new EmptyResult();
        }
        catch (InferenceTimeoutException exception)
        {
            await TraceAsync(requestId, "Backend", "error", "Backend-Timeout", exception.Message).ConfigureAwait(false);
            if (Response.HasStarted)
                await WriteSseErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
            else
                return StatusCode(StatusCodes.Status504GatewayTimeout, CreateError(exception.Message, "timeout"));

            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            await TraceAsync(requestId, "Backend", "error", "Backend-Timeout", "Lokale Inferenz hat das Zeitlimit erreicht").ConfigureAwait(false);
            return StatusCode(StatusCodes.Status504GatewayTimeout, CreateError("Local inference request timed out.", "timeout"));
        }
        catch (HttpRequestException)
        {
            await TraceAsync(requestId, "Backend", "error", "Backend nicht erreichbar", "Das lokale Backend konnte nicht erreicht werden").ConfigureAwait(false);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, CreateError("The local backend is unavailable.", "backend_error"));
        }
        catch (ArgumentException exception)
        {
            await TraceAsync(requestId, "API", "error", "Ungültige Anfrage", exception.Message).ConfigureAwait(false);
            return BadRequest(CreateError(exception.Message, "invalid_request_error"));
        }
        catch (KeyNotFoundException exception)
        {
            await TraceAsync(requestId, "Routing", "error", "Konfiguration nicht gefunden", exception.Message).ConfigureAwait(false);
            if (Response.HasStarted)
                await WriteSseErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
            else
                return NotFound(CreateError(exception.Message, "model_not_found"));

            return new EmptyResult();
        }
        catch (InvalidOperationException exception)
        {
            await TraceAsync(requestId, "Backend", "error", "Backend konnte nicht starten", exception.Message).ConfigureAwait(false);
            if (Response.HasStarted)
                await WriteSseErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
            else
                return StatusCode(StatusCodes.Status503ServiceUnavailable, CreateError(exception.Message, "server_error"));

            return new EmptyResult();
        }
        catch (Exception exception) when (Response.HasStarted)
        {
            await TraceAsync(requestId, "Backend", "error", "Backend-Fehler", exception.Message).ConfigureAwait(false);
            await WriteSseErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
            return new EmptyResult();
        }
    }

    private async Task StreamCompletionAsync(
        string requestId,
        OpenAiChatRequest request,
        bool includeUsage,
        CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");

        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        await WriteSseAsync(CreateChunk(completionId, request.Model ?? string.Empty, new OpenAiChatCompletionDelta("assistant"), null), cancellationToken).ConfigureAwait(false);

        var backendRequest = await PrepareBackendRequestAsync(request, requestId, completionId, cancellationToken).ConfigureAwait(false);

        var structuredToolOutput = request.Tools is { Count: > 0 };
        var deltas = Channel.CreateUnbounded<string>();
        var generationTask = backendMiddleware.GenerateAsync(
            backendRequest,
            structuredToolOutput
                ? null
                : delta =>
                {
                    deltas.Writer.TryWrite(delta);
                    return Task.CompletedTask;
                },
            cancellationToken);
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
                    await WriteSseHeartbeatAsync(completionId, backendRequest.Model, cancellationToken).ConfigureAwait(false);
                    heartbeatTask = Task.Delay(SseHeartbeatInterval, cancellationToken);
                    continue;
                }

                if (!await readTask.ConfigureAwait(false))
                    break;

                while (deltas.Reader.TryRead(out var delta))
                {
                    if (!string.IsNullOrEmpty(delta))
                        await WriteSseAsync(CreateChunk(completionId, backendRequest.Model, new OpenAiChatCompletionDelta(Content: delta), null), cancellationToken).ConfigureAwait(false);
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
        await TraceGenerationResultAsync(requestId, generationResult).ConfigureAwait(false);
        var finalDelta = structuredToolOutput
            ? new OpenAiChatCompletionDelta(
                Content: string.IsNullOrEmpty(generationResult.Text) ? null : generationResult.Text,
                ToolCalls: ToToolCallDeltas(generationResult.ToolCalls))
            : new OpenAiChatCompletionDelta();
        await WriteSseAsync(CreateChunk(
            completionId,
            backendRequest.Model,
            finalDelta,
            generationResult.FinishReason,
            includeUsage ? CreateUsage(generationResult) : null), cancellationToken).ConfigureAwait(false);
        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<OpenAiBackendChatRequest> PrepareBackendRequestAsync(
        OpenAiChatRequest request,
        string requestId,
        string? completionId,
        CancellationToken cancellationToken)
    {
        var status = await EnsureConfigurationLoadedAsync(request.Model, requestId, completionId, cancellationToken).ConfigureAwait(false);
        var backendRequest = backendMiddleware.Prepare(request, status);
        if (dataService is not null && !string.IsNullOrWhiteSpace(status.ModelPath))
        {
            var configurationBackend = status.Backend is "Vulkan" or "VULKAN" or "CUDA" or "SYCL" or "CPU"
                ? ConfigurationBackend.Llama
                : Enum.TryParse<ConfigurationBackend>(status.Backend, true, out var parsedBackend)
                    ? parsedBackend
                    : (ConfigurationBackend?)null;
            var timeout = configurationBackend is ConfigurationBackend backend
                ? await dataService.GetActiveInferenceTimeoutAsync(status.ModelPath, backend, cancellationToken).ConfigureAwait(false)
                : null;
            if (timeout is not null)
                backendRequest = backendRequest with { InferenceTimeout = timeout };
        }

        await TraceAsync(
            requestId,
            "Routing",
            "out",
            "An lokales Backend geroutet",
            $"{backendRequest.Backend} · Modell {backendRequest.Model}",
            SerializeTracePayload(new
            {
                backendRequest.Backend,
                backendRequest.Model,
                MessageCount = backendRequest.Messages.Count,
                ToolCount = backendRequest.Tools?.Count ?? 0,
                backendRequest.InferenceTimeout
            })).ConfigureAwait(false);
        await TraceAsync(
            requestId,
            "Backend",
            "out",
            "Request an Backend",
            $"{backendRequest.Backend} verarbeitet die Anfrage",
            SerializeTracePayload(backendRequest)).ConfigureAwait(false);

        return backendRequest;
    }

    private async Task<ModelLoadStatus> EnsureConfigurationLoadedAsync(
        string? identifier,
        string requestId,
        string? completionId,
        CancellationToken cancellationToken)
    {
        if (dataService is null)
            return GetLoadedModelStatus(identifier);

        var configuration = await dataService.ResolveApiModelConfigurationAsync(identifier, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"The model configuration '{identifier}' was not found.");
        var currentStatus = modelRuntime.LoadedModel_Read();
        var matchingModel = currentStatus.LoadedModels.FirstOrDefault(model =>
            string.Equals(model.ModelPath, configuration.ModelPath, StringComparison.OrdinalIgnoreCase) &&
            model.Backend == configuration.Backend);
        if (matchingModel is null)
        {
            if (!configuration.AutoLaunch)
                throw new InvalidOperationException($"The configuration '{configuration.Name}' is not loaded and AutoLaunch is disabled.");

            await TraceAsync(
                requestId,
                "Routing",
                "out",
                "AutoLaunch gestartet",
                $"Konfiguration {configuration.Name} wird geladen",
                SerializeTracePayload(new { configuration.Id, configuration.Name, configuration.ModelPath, configuration.Backend })).ConfigureAwait(false);
        }
        else if (!matchingModel.IsLoading)
        {
            return GetLoadedModelStatus(currentStatus, configuration.ModelPath, configuration.Backend);
        }

        var loadTask = dataService.LoadModelConfigurationAsync(configuration.Id, cancellationToken);
        ModelLoadStatus loadedStatus;
        if (completionId is null)
        {
            loadedStatus = await loadTask.ConfigureAwait(false);
        }
        else
        {
            while (!loadTask.IsCompleted)
            {
                var completedTask = await Task.WhenAny(
                    loadTask,
                    Task.Delay(SseHeartbeatInterval, cancellationToken)).ConfigureAwait(false);
                if (completedTask != loadTask)
                    await WriteSseHeartbeatAsync(completionId, configuration.Name, cancellationToken).ConfigureAwait(false);
            }

            loadedStatus = await loadTask.ConfigureAwait(false);
        }

        await TraceAsync(requestId, "Backend", "in", "Konfiguration geladen", $"{configuration.Name} ist bereit").ConfigureAwait(false);
        return GetLoadedModelStatus(loadedStatus, configuration.ModelPath, configuration.Backend);
    }

    private ModelLoadStatus GetLoadedModelStatus(string? requestedModel, ConfigurationBackend? requestedBackend = null) =>
        GetLoadedModelStatus(modelRuntime.LoadedModel_Read(), requestedModel, requestedBackend);

    private ModelLoadStatus GetLoadedModelStatus(
        ModelLoadStatus status,
        string? requestedModel,
        ConfigurationBackend? requestedBackend = null)
    {
        if (!status.IsModelLoaded || string.IsNullOrWhiteSpace(status.Backend))
            throw new InvalidOperationException("No model is currently loaded.");

        if (string.IsNullOrWhiteSpace(requestedModel))
            return status;

        var loadedModel = status.LoadedModels.FirstOrDefault(model =>
            (string.Equals(model.ModelPath, requestedModel, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetFileName(model.ModelPath), requestedModel, StringComparison.OrdinalIgnoreCase)) &&
            (!requestedBackend.HasValue || model.Backend == requestedBackend));
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
            Devices = loadedModel.Devices,
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
        return new OpenAiUsage(result.PromptTokenCount, result.TokenCount, totalTokens, result.TokensPerSecond, result.TimeToFirstTokenMs, result.PrefillDurationMs, result.DecodeDurationMs);
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
        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task WriteSseHeartbeatAsync(string completionId, string model, CancellationToken cancellationToken) =>
        WriteSseAsync(CreateChunk(completionId, model, new OpenAiChatCompletionDelta(), null), cancellationToken);

    private async Task TraceGenerationResultAsync(string requestId, GenerationResult result)
    {
        await TraceAsync(
            requestId,
            "Backend",
            "in",
            "Antwort vom Backend",
            $"{result.TokenCount} Tokens · {result.TokensPerSecond:0.##} tok/s · finish_reason={result.FinishReason}",
            SerializeTracePayload(new
            {
                result.Text,
                result.TokenCount,
                DurationMilliseconds = result.Duration.TotalMilliseconds,
                result.TokensPerSecond,
                result.PromptTokenCount,
                result.TimeToFirstTokenMs,
                result.PrefillDurationMs,
                result.DecodeDurationMs,
                result.FinishReason,
                result.ToolCalls
            })).ConfigureAwait(false);
    }

    private async Task TraceAsync(
        string requestId,
        string layer,
        string direction,
        string title,
        string detail,
        string? payload = null)
    {
        if (providerTraceStore is null)
            return;

        try
        {
            await providerTraceStore.PublishAsync(
                new ProviderTraceEntry(Guid.NewGuid(), requestId, DateTimeOffset.UtcNow, layer, direction, title, detail, payload),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string? SerializeTracePayload<T>(T? value)
    {
        if (value is null)
            return null;

        var payload = JsonSerializer.Serialize(value, SseJsonOptions);
        return payload.Length <= 4000 ? payload : $"{payload[..4000]}...";
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
