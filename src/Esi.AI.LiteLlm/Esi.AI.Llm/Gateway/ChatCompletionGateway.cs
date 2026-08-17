using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Esi.AI.Llm;
using Esi.AI.Llm.Providers;
using Esi.AI.Llm.Router;

namespace Esi.AI.Llm.Gateway;

/// <summary>
/// Gateway für OpenAI-kompatible Chat-Completion-Endpunkte.
/// Stellt ASP.NET Core Endpunkte bereit, die mit bestehenden OpenAI-kompatiblen Clients verwendet werden können.
/// </summary>
public class ChatCompletionGateway
{
    private readonly Orchestrator _orchestrator;
    private readonly ILogger<ChatCompletionGateway>? _logger;

    public ChatCompletionGateway(Orchestrator orchestrator, ILogger<ChatCompletionGateway>? logger = null)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Registriert die Endpunkte bei einer WebApplication.
    /// </summary>
    /// <param name="app">Die WebApplication-Instanz.</param>
    public void RegisterEndpoints(WebApplication app)
    {
        // POST /v1/chat/completions (non-streaming + SSE streaming)
        app.MapPost("/v1/chat/completions", async (
            [FromBody] ChatCompletionRequest request,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (request?.Messages?.Any() != true)
                return Results.BadRequest("Messages must contain at least one entry.");
            if (string.IsNullOrWhiteSpace(request.Model))
                return Results.BadRequest("Model must be specified.");

            if (request.Stream == true)
            {
                await WriteStreamingResponseAsync(httpContext, _orchestrator, request, _logger, ct);
                return Results.Empty;
            }

            try
            {
                var result = await _orchestrator.OrchestrateAsync(request, cancellationToken: ct);

                if (result.Error != null)
                    return Results.Problem(result.Error.Reason,
                        statusCode: result.Error.StatusCode);

                // Build OpenAI-compatible response
                var responseDict = new Dictionary<string, object>
                {
                    ["id"] = result.Id,
                    ["object"] = "chat.completion",
                    ["model"] = request.Model,
                    ["choices"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["index"] = 0,
                            ["message"] = new Dictionary<string, object>
                            {
                                ["role"] = "assistant",
                                ["content"] = result.Content!
                            },
                            ["finish_reason"] = result.FinishReason
                        }
                    }
                };

                // Add usage if available
                if (result.Usage != null)
                {
                    responseDict["usage"] = new Dictionary<string, object>
                    {
                        ["prompt_tokens"] = result.Usage.InputTokens,
                        ["completion_tokens"] = result.Usage.OutputTokens,
                        ["total_tokens"] = result.Usage.TotalTokens
                    };
                }

                return Results.Ok(responseDict);
            }
            catch (Exception ex) when (ex is OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Chat completion request cancelled");
                return Results.Problem("Request was cancelled", statusCode: (int)HttpStatusCode.GatewayTimeout);
            }
        });

        // GET /v1/models (optional)
        app.MapGet("/v1/models", async (
            CancellationToken ct) =>
        {
            // In einer vollständigen Implementierung würden hier die verfügbaren Models
            // aus der Konfiguration oder dem Provider zurückgegeben werden.
            var models = new[]
            {
                new { id = "gpt-4o", Model = "model", created = 0, owned_by = "openai" },
                new { id = "gpt-4o-mini", Model = "model", created = 0, owned_by = "openai" }
            };

            return Results.Ok(models);
        });
    }

    /// <summary>
    /// Streamt eine Chat-Completion-Anfrage als Server-Sent-Events (OpenAI-kompatibles Chunk-Format).
    /// </summary>
    private static async Task WriteStreamingResponseAsync(
        HttpContext httpContext,
        Orchestrator orchestrator,
        ChatCompletionRequest request,
        ILogger<ChatCompletionGateway>? logger,
        CancellationToken cancellationToken)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in orchestrator.OrchestrateStreamingAsync(request, cancellationToken))
            {
                var payload = new Dictionary<string, object?>
                {
                    ["id"] = chunk.Id,
                    ["object"] = "chat.completion.chunk",
                    ["model"] = request.Model,
                    ["choices"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["index"] = 0,
                            ["delta"] = new Dictionary<string, object?> { ["content"] = chunk.Content },
                            ["finish_reason"] = chunk.FinishReason
                        }
                    }
                };

                await httpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            await httpContext.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client hat die Verbindung abgebrochen; Response-Header wurden bereits gesendet.
            logger?.LogDebug("Streaming chat completion request was cancelled by the client.");
        }
    }
}

/// <summary>
/// Erlaubt Hosts, die LiteLLM-Gateway-Endpunkte mit einer Zeile über DI zu registrieren.
/// </summary>
public static class ChatCompletionGatewayExtensions
{
    /// <summary>
    /// Löst den Orchestrator aus DI auf und registriert die OpenAI-kompatiblen Gateway-Endpunkte.
    /// </summary>
    public static WebApplication MapLiteLlmGateway(this WebApplication app)
    {
        var orchestrator = app.Services.GetRequiredService<Orchestrator>();
        var logger = app.Services.GetService<ILogger<ChatCompletionGateway>>();
        new ChatCompletionGateway(orchestrator, logger).RegisterEndpoints(app);
        return app;
    }
}