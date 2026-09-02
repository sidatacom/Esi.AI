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
                .Select(model => new OpenAiModel(model.Path, "model", created, "esi-ai", model.Name, model.Capabilities, IsLoaded(model.Path)))
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
            var messages = request!.Messages!.Select(message => new LlamaChatMessage(message.Role, message.Content ?? string.Empty)).ToArray();
            var model = string.IsNullOrWhiteSpace(request.Model) ? GetModelId(status) : request.Model;

            if (request.Stream)
            {
                await StreamCompletionAsync(status.Backend!, status.ModelPath, messages, model, cancellationToken).ConfigureAwait(false);
                return new EmptyResult();
            }

            var result = await GenerateAsync(status.Backend!, status.ModelPath, messages, null, cancellationToken).ConfigureAwait(false);
            return Ok(CreateCompletion(result.Text, model));
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

    private async Task StreamCompletionAsync(string backend, string? modelPath, IReadOnlyList<LlamaChatMessage> messages, string model, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");

        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        await WriteSseAsync(CreateChunk(completionId, model, new OpenAiChatCompletionDelta("assistant"), null), cancellationToken).ConfigureAwait(false);

        var deltas = Channel.CreateUnbounded<string>();
        var generationTask = GenerateAsync(backend, modelPath, messages, delta =>
        {
            deltas.Writer.TryWrite(delta);
            return Task.CompletedTask;
        }, cancellationToken);
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

        await WriteSseAsync(CreateChunk(completionId, model, new OpenAiChatCompletionDelta(), "stop"), cancellationToken).ConfigureAwait(false);
        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<LlamaGenerationResult> GenerateAsync(string backend, string? modelPath, IReadOnlyList<LlamaChatMessage> messages, Func<string, Task>? onDelta, CancellationToken cancellationToken) =>
        backend switch
        {
            "OpenVINO" => StartOpenVinoGeneration(messages, onDelta, cancellationToken),
            "vLLM" or "SGLang" => GeneratePythonAsync(messages, onDelta, cancellationToken),
            "dotLLM" => GenerateDotLlmAsync(messages, onDelta, cancellationToken),
            "Vulkan" or "VULKAN" or "CPU" => GenerateLlamaAsync(modelPath, messages, onDelta, cancellationToken),
            _ => throw new ArgumentException($"Unsupported model backend '{backend}'.", nameof(backend))
        };

    private Task<LlamaGenerationResult> StartOpenVinoGeneration(
        IReadOnlyList<LlamaChatMessage> messages,
        Func<string, Task>? onDelta,
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
            var result = session.GenerateWithStats(CreateOpenVinoPrompt(messages), streamer);
            return new LlamaGenerationResult(result.Text, result.TokenCount, TimeSpan.Zero, result.TokensPerSecond);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task<LlamaGenerationResult> GenerateLlamaAsync(string? modelPath, IReadOnlyList<LlamaChatMessage> messages, Func<string, Task>? onDelta, CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreateLlamaChatSession("You are a helpful assistant.", modelPath);
        return await session.GenerateWithStatsAsync(messages, onDelta, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LlamaGenerationResult> GeneratePythonAsync(IReadOnlyList<LlamaChatMessage> messages, Func<string, Task>? onDelta, CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreatePythonChatSession();
        return await session.GenerateWithStatsAsync(messages, onDelta, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LlamaGenerationResult> GenerateDotLlmAsync(IReadOnlyList<LlamaChatMessage> messages, Func<string, Task>? onDelta, CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreateDotLlmChatSession();
        return await session.GenerateWithStatsAsync(messages, onDelta, cancellationToken).ConfigureAwait(false);
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
        if (!allowToolCalls && request.Messages.Any(message => string.IsNullOrWhiteSpace(message.Content)))
            return CreateError("Every chat message requires a non-empty role and content.", "invalid_request_error");
        return null;
    }

    private static string GetModelId(ModelLoadStatus status) =>
        string.IsNullOrWhiteSpace(status.ModelPath) ? "local-model" : Path.GetFileNameWithoutExtension(status.ModelPath);

    private static string CreateOpenVinoPrompt(IReadOnlyList<LlamaChatMessage> messages)
    {
        if (messages.Count == 1)
            return messages[0].Content;

        return string.Join("\n", messages.Select(message => $"{message.Role}: {message.Content}")) + "\nassistant:";
    }

    private static OpenAiChatCompletionResponse CreateCompletion(string content, string model) =>
        new($"chatcmpl-{Guid.NewGuid():N}", "chat.completion", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
            new[] { new OpenAiChatCompletionChoice(0, new OpenAiChatMessage("assistant", content), "stop") });

    private static OpenAiChatCompletionChunk CreateChunk(string id, string model, OpenAiChatCompletionDelta delta, string? finishReason) =>
        new(id, "chat.completion.chunk", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
            new[] { new OpenAiChatCompletionChunkChoice(0, delta, finishReason) });

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

    private static async Task CompleteChannelAsync(Task<LlamaGenerationResult> generationTask, ChannelWriter<string> writer)
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
