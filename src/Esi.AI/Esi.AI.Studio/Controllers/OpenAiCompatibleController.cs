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
        var status = modelRuntime.GetStatus();
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
            using var session = modelRuntime.CreateLlamaChatSession("You are a helpful assistant.");
            var messages = request.Messages.Select(message => new LlamaChatMessage(message.Role, message.Content)).ToArray();
            var model = request.Model ?? "local-model";

            if (!request.Stream)
            {
                var content = await session.GenerateAsync(messages, cancellationToken);
                return Ok(new
                {
                    id = $"chatcmpl-{Guid.NewGuid():N}",
                    @object = "chat.completion",
                    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    model,
                    choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } }
                });
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
}

public sealed record OpenAiChatRequest(string? Model, IReadOnlyList<OpenAiChatMessage> Messages, bool Stream = false);

public sealed record OpenAiChatMessage(string Role, string Content);
