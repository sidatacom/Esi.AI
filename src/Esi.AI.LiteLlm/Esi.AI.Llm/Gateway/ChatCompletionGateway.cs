using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Esi.AI.Llm;
using Esi.AI.Llm.Providers;
using Esi.AI.Llm.Router;

/// <summary>
/// Gateway für OpenAI-kompatible Chat-Completion-Endpunkte.
/// Stellt ASP.NET Core Endpunkte bereit, die mit bestehenden OpenAI-kompatiblen Clients verwendet werden können.
/// </summary>
public class ChatCompletionGateway
{
    private readonly ProviderRouter _router;
    private readonly ILogger<ChatCompletionGateway>? _logger;

    public ChatCompletionGateway(ProviderRouter router, ILogger<ChatCompletionGateway>? logger = null)
    {
        _router = router;
        _logger = logger;
    }

    /// <summary>
    /// Registriert die Endpunkte bei einer WebApplication.
    /// </summary>
    /// <param name="app">Die WebApplication-Instanz.</param>
    public void RegisterEndpoints(WebApplication app)
    {
        // POST /v1/chat/completions (non-streaming)
        app.MapPost("/v1/chat/completions", async (
            [FromBody] ChatCompletionRequest request,
            CancellationToken ct) =>
        {
            if (request?.Messages?.Any() != true)
                return Results.BadRequest("Messages must contain at least one entry.");
            if (string.IsNullOrWhiteSpace(request.Model))
                return Results.BadRequest("Model must be specified.");

            try
            {
                var result = await _router.CompleteAsync(request, cancellationToken: ct);

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
}