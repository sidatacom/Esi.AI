using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Esi.AI.Llm.Providers;

/// <summary>
/// Google Gemini-kompatibler Chat-Completion-Provider.
/// Kommuniziert mit der Google Gemini API (oder jedem anderen Gemini-kompatiblen Endpoint).
/// </summary>
public class GeminiProvider : IChatCompletionProvider
{
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Initialisiert einen neuen Gemini-Provider.
    /// </summary>
    /// <param name="apiKey">API-Key für die Google Gemini API.</param>
    /// <param name="endpoint">API-Endpoint (z.B. "https://generativelanguage.googleapis.com/v1beta").</param>
    public GeminiProvider(string apiKey, string endpoint = "https://generativelanguage.googleapis.com/v1beta")
    {
        _apiKey = apiKey;
        _endpoint = endpoint;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_endpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);
    }

    /// <summary>
    /// Eindeutiger Name des Providers.
    /// </summary>
    public string Name => "gemini";

    /// <summary>
    /// Nicht-streaming Chat Completion über den Gemini Provider.
    /// </summary>
    public async Task<ProviderResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = string.Join("\n", request.Messages.Select(m => $"{m.Role}: {m.Content}")) }
                    }
                }
            },
            temperature = request.Temperature,
            maxOutputTokens = request.MaxTokens
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var url = $"{_endpoint}:generateContent?key={_apiKey}";
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);

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
                    Reason = response.ReasonPhrase ?? "Unknown error",
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

        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var contentElem))
        {
            if (contentElem.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textElem))
            {
                contentText = textElem.GetString();
            }
            finishReason = candidates[0].GetProperty("finishReason").GetString();
        }

        int inputTokens = 0, outputTokens = 0;
        if (doc.RootElement.TryGetProperty("promptTokenCount", out var promptTokens))
            inputTokens = promptTokens.GetInt32();
        if (doc.RootElement.TryGetProperty("candidates", out var cand) &&
            cand.GetArrayLength() > 0 &&
            cand[0].TryGetProperty("outputTokenCount", out var output))
            outputTokens = output.GetInt32();

        return new ProviderResult
        {
            Id = Guid.NewGuid().ToString(),
            Content = contentText ?? string.Empty,
            FinishReason = finishReason ?? "stop",
            Usage = new ProviderResult.UsageInfo
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens
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
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = string.Join("\n", request.Messages.Select(m => m.Content)) }
                    }
                }
            },
            stream = true
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var query = $"?key={_apiKey}";
        var requestUri = _httpClient.BaseAddress?.AbsoluteUri + query;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

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
                    string? chunkContent = null;
                    string? finishReason = null;

                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        chunkContent = choices[0].GetProperty("delta").TryGetProperty("content", out var c) ? c.GetString() : null;
                        finishReason = choices[0].GetProperty("finish_reason").GetString();
                    }

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
}