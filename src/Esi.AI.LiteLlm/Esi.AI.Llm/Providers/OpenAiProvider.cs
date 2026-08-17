using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using Esi.AI.Llm.Models;

namespace Esi.AI.Llm.Providers;

/// <summary>
/// OpenAI-kompatibler Chat-Completion-Provider.
/// Kommuniziert mit der OpenAI API (oder jedem anderen OpenAI-kompatiblen Endpunkt).
/// </summary>
public class OpenAiProvider : IChatCompletionProvider
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Eindeutiger Name des Providers.
    /// </summary>
    public string Name => "openai";

    /// <summary>
    /// Initialisiert einen neuen OpenAI-Provider.
    /// </summary>
    /// <param name="apiKey">API-Key für die OpenAI API.</param>
    /// <param name="endpoint">API-Endpoint (z.B. "https://api.openai.com/v1").</param>
    /// <param name="httpClient">Von <see cref="IHttpClientFactory"/> erzeugter HTTP-Client.</param>
    public OpenAiProvider(string apiKey, string endpoint, HttpClient httpClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        if (!_httpClient.DefaultRequestHeaders.Contains("Authorization"))
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    /// <summary>
    /// Nicht-streaming Chat Completion über den OpenAI Provider.
    /// </summary>
    public async Task<ProviderResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Build payload with SnakeCase naming
        var payload = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            stream = false
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ProviderResult
            {
                Id = Guid.NewGuid().ToString(),
                Content = string.Empty,
                FinishReason = "error",
                Error = new ProviderResult.ErrorInfo
                {
                    Reason = string.IsNullOrWhiteSpace(errorBody) ? response.ReasonPhrase ?? "Unknown error" : errorBody,
                    Code = ((int)response.StatusCode).ToString(),
                    StatusCode = (int)response.StatusCode,
                    IsRetryable = response.StatusCode >= HttpStatusCode.InternalServerError && response.StatusCode <= HttpStatusCode.GatewayTimeout
                }
            };
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);

        string? contentText = null;
        string? finishReason = null;

        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            var messageElement = choice.TryGetProperty("message", out var m) ? m : choice.GetProperty("delta");
            contentText = messageElement.TryGetProperty("content", out var c) ? c.GetString() : null;
            finishReason = choice.GetProperty("finish_reason").GetString();
        }

        int? inputTokens = null;
        int? outputTokens = null;

        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var prompt))
                inputTokens = prompt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var completion))
                outputTokens = completion.GetInt32();
        }

        return new ProviderResult
        {
            Id = Guid.NewGuid().ToString(),
            Content = contentText ?? string.Empty,
            FinishReason = finishReason ?? "end_turn",
            Usage = new ProviderResult.UsageInfo
            {
                InputTokens = inputTokens ?? 0,
                OutputTokens = outputTokens ?? 0,
                TotalTokens = (inputTokens ?? 0) + (outputTokens ?? 0)
            }
        };
    }

    /// <summary>
    /// Streaming Chat Completion. Gibt Token-Folgen zurück.
    /// </summary>
    public async IAsyncEnumerable<Chunk> CompleteStreamingAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            stream = true
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            yield return new Chunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = string.Empty,
                FinishReason = "error"
            };
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line) || line == "data: [DONE]")
                continue;

            if (line.StartsWith("data: "))
            {
                var jsonLine = line.Substring("data: ".Length);
                using var doc = JsonDocument.Parse(jsonLine);

                if (doc.RootElement.TryGetProperty("id", out var idElem))
                {
                    string? chunkContent = doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                        ? choices[0].GetProperty("delta").TryGetProperty("content", out var c) ? c.GetString() : null
                        : null;

                    string? finishReason = doc.RootElement.TryGetProperty("choices", out var cr) && cr.GetArrayLength() > 0
                        ? cr[0].GetProperty("finish_reason").GetString() : null;

                    yield return new Chunk
                    {
                        Id = idElem.GetString() ?? Guid.NewGuid().ToString(),
                        Content = chunkContent,
                        FinishReason = finishReason
                    };
                }
            }
        }
    }

    /// <summary>
    /// Fehlerinformationen innerhalb von ProviderResult.
    /// </summary>
    public sealed class ErrorInfo
    {
        /// <summary>Fehlermeldung.</summary>
        public string? Reason { get; set; }

        /// <summary>Fehlercode.</summary>
        public string? Code { get; set; }

        /// <summary>HTTP-Statuscode.</summary>
        public int? StatusCode { get; set; }

        /// <summary>Ist dieser Fehler vorübergehend (retry-fähig)?</summary>
        public bool IsRetryable { get; set; }
    }
}
