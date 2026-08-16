using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Esi.AI.Llm.Providers;

/// <summary>
/// Google Gemini API Chat-Completion-Provider.
/// </summary>
public class GoogleGeminiProvider : IChatCompletionProvider
{
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Unedeger Name of the Provider.
    /// </summary>
    public string Name => "google_gemini";

    /// <summary>
    /// Initializes a new Google Gemini Provider.
    /// </summary>
    /// <param name="apiKey">API-Key for the Google Gemini API.</param>
    /// <param name="endpoint">API-Endpoint (default: "https://google.com/v1/gemini").</param>
    public GoogleGeminiProvider(string apiKey, string endpoint = "https://google.com/v1/gemini")
    {
        _apiKey = apiKey;
        _endpoint = endpoint;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_endpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("content-type", "application/json");
    }

    /// <summary>
    /// Non-streaming Chat Completion via the Google Gemini Provider.
    /// </summary>
    public async Task<ProviderResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = request.MaxTokens,
            temperature = request.Temperature
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/v1/messages", content, cancellationToken);

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

        if (doc.RootElement.TryGetProperty("content", out var contentElem))
        {
            if (contentElem.TryGetProperty("0", out var firstContentElem) &&
                firstContentElem.TryGetProperty("text", out var textElem))
            {
                contentText = textElem.GetString();
            }
        }

        if (doc.RootElement.TryGetProperty("stop_reason", out var stopReasonElem))
        {
            finishReason = stopReasonElem.GetString();
        }

        return new ProviderResult
        {
            Id = Guid.NewGuid().ToString(),
            Content = contentText ?? string.Empty,
            FinishReason = finishReason ?? "end_turn",
            Usage = new ProviderResult.UsageInfo
            {
                InputTokens = doc.RootElement.GetProperty("usage").GetProperty("input_tokens").GetInt32(),
                OutputTokens = doc.RootElement.GetProperty("usage").GetProperty("output_tokens").GetInt32(),
                TotalTokens = doc.RootElement.GetProperty("usage").GetProperty("total_tokens").GetInt32()
            }
        };
    }

    /// <summary>
    /// Streaming Chat Completion. Gives Token-Follows back.
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

        using var response = await _httpClient.PostAsync("/v1/messages", content, cancellationToken);

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
}
