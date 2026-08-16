using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Esi.AI.Llm.Providers;

/// <summary>
/// Ollama-kompatibler Chat-Completion-Provider.
/// Kommuniziert mit der lokalen Ollama API.
/// </summary>
public class OllamaProvider : IChatCompletionProvider
{
    private readonly string _endpoint;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Initialisiert einen neuen Ollama-Provider.
    /// </summary>
    /// <param name="endpoint">Ollama-Endpoint (z.B. "http://localhost:11434").</param>
    public OllamaProvider(string endpoint = "http://localhost:11434")
    {
        _endpoint = endpoint;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_endpoint)
        };
    }

    /// <summary>
    /// Eindeutiger Name des Providers.
    /// </summary>
    public string Name => "ollama";

    /// <summary>
    /// Nicht-streaming Chat Completion über den Ollama Provider.
    /// </summary>
    public async Task<ProviderResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = request.Model ?? "llama2",
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false,
            options = new
            {
                temperature = request.Temperature,
                num_predict = request.MaxTokens
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/api/chat", content, cancellationToken);

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

        if (doc.RootElement.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var text))
        {
            contentText = text.GetString();
        }

        if (doc.RootElement.TryGetProperty("done_reason", out var doneReason))
        {
            finishReason = doneReason.GetString();
        }

        return new ProviderResult
        {
            Id = Guid.NewGuid().ToString(),
            Content = contentText ?? string.Empty,
            FinishReason = finishReason ?? "stop",
            Usage = new ProviderResult.UsageInfo
            {
                InputTokens = doc.RootElement.GetProperty("prompt_eval_count").GetInt32(),
                OutputTokens = doc.RootElement.GetProperty("eval_count").GetInt32(),
                TotalTokens = doc.RootElement.GetProperty("prompt_eval_count").GetInt32() + doc.RootElement.GetProperty("eval_count").GetInt32()
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
            model = request.Model ?? "llama2",
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = true,
            options = new
            {
                temperature = request.Temperature,
                num_predict = request.MaxTokens
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync("/api/chat", content, cancellationToken);

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

                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c))
                {
                    string? chunkContent = c.GetString();

                    yield return new Chunk
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = chunkContent,
                        FinishReason = null
                    };
                }
            }
        }
    }
}