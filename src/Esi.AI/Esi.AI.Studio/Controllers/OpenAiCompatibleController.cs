using System.Text.Json;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Microsoft.AspNetCore.Mvc;

namespace Esi.AI.Studio.Controllers;

[ApiController]
[Route("v1")]
public sealed class OpenAiCompatibleController(ModelRuntime modelRuntime) : ControllerBase
{
    [HttpGet("models")]
    public IActionResult ListModels()
    {
        var status = modelRuntime.LoadedModel_Read();
        var modelId = status.ModelPath is null ? "local-model" : Path.GetFileNameWithoutExtension(status.ModelPath);
        return Ok(new
        {
            @object = "list",
            data = new[] { new { id = modelId, @object = "model", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), owned_by = "esi-ai" } }
        });
    }

    [HttpPost("chat/completions")]
    public async Task<IActionResult> CreateChatCompletion(
        OpenAiChatRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Messages is null || request.Messages.Count == 0)
            return BadRequest(new { error = new { message = "At least one chat message is required.", type = "invalid_request_error" } });

        try
        {
            var messages = request.Messages.Select(message => new LlamaChatMessage(message.Role, message.Content)).ToArray();
            var model = request.Model ?? "local-model";
            var backend = modelRuntime.LoadedModel_Read().Backend;

            if (backend is "vLLM" or "SGLang" or "dotLLM")
            {
                var generation = backend switch
                {
                    "dotLLM" => GenerateDotLlmAsync(messages, cancellationToken),
                    _ => GeneratePythonAsync(messages, cancellationToken)
                };
                var result = await generation;
                return Ok(CreateCompletion(result.Text, model));
            }

            if (backend == "OpenVINO")
            {
                using var openVinoSession = modelRuntime.CreateOpenVinoChatSession();
                var prompt = messages[^1].Content;
                var result = await Task.Run(() => openVinoSession.GenerateWithStats(prompt), cancellationToken);
                return Ok(CreateCompletion(result.Text, model));
            }

            using var session = modelRuntime.CreateLlamaChatSession("You are a helpful assistant.");

            if (!request.Stream)
            {
                var content = await session.GenerateAsync(messages, cancellationToken);
                return Ok(CreateCompletion(content, model));
            }

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            var completionId = $"chatcmpl-{Guid.NewGuid():N}";
            await foreach (var token in session.GenerateStreamingAsync(messages, cancellationToken))
            {
                var chunk = new
                {
                    id = completionId,
                    @object = "chat.completion.chunk",
                    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    model,
                    choices = new[] { new { index = 0, delta = new { content = token }, finish_reason = (string?)null } }
                };
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return new EmptyResult();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = new { message = exception.Message, type = "server_error" } });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = new { message = exception.Message, type = "invalid_request_error" } });
        }
    }

    private async Task<LlamaGenerationResult> GeneratePythonAsync(IReadOnlyList<LlamaChatMessage> messages, CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreatePythonChatSession();
        return await session.GenerateWithStatsAsync(messages, cancellationToken);
    }

    private async Task<LlamaGenerationResult> GenerateDotLlmAsync(IReadOnlyList<LlamaChatMessage> messages, CancellationToken cancellationToken)
    {
        using var session = modelRuntime.CreateDotLlmChatSession();
        return await session.GenerateWithStatsAsync(messages, cancellationToken);
    }

    private static object CreateCompletion(string content, string model) => new
    {
        id = $"chatcmpl-{Guid.NewGuid():N}",
        @object = "chat.completion",
        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        model,
        choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } }
    };
}

public sealed record OpenAiChatRequest(string? Model, IReadOnlyList<OpenAiChatMessage> Messages, bool Stream = false);

public sealed record OpenAiChatMessage(string Role, string Content);
