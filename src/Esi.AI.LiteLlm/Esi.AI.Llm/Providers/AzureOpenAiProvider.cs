using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Esi.AI.Llm.Providers;

/// <summary>
/// Azure OpenAI-kompatibler Chat-Completion-Provider.
/// Kommuniziert mit der Azure OpenAI API.
/// </summary>
public class AzureOpenAiProvider : IChatCompletionProvider
{
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly string _deploymentName;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Initialisiert einen neuen Azure OpenAI-Provider.
    /// </summary>
    /// <param name="apiKey">API-Key für die Azure OpenAI API.</param>
    /// <param name="endpoint">API-Endpoint (z.B. "https://your-resource.openai.azure.com").</param>
    /// <param name="deploymentName">Name der Deployment-Ressource.</param>
    public AzureOpenAiProvider(string apiKey, string endpoint, string deploymentName)
    {
        _apiKey = apiKey;
        _endpoint = endpoint;
        _deploymentName = deploymentName;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_endpoint)
        };
        _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);
    }

    /// <summary>
    /// Eindeutiger Name des Providers.
    /// </summary>
    public string Name => "azure_openai";

    /// <summary>
    /// Nicht-streaming Chat Completion über den Azure OpenAI Provider.
    /// </summary>
    public async Task<ProviderResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            model = _deploymentName,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var url = $"/openai/deployments/{_deploymentName}/chat/completions?api-version=2024-02-15";
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

        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            if (choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var text))
            {
                contentText = text.GetString();
            }
            finishReason = choices[0].GetProperty("finish_reason").GetString();
        }

        int inputTokens = 0, outputTokens = 0;
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
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            model = _deploymentName,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            stream = true
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync($"/openai/deployments/{_deploymentName}/chat/completions", content, cancellationToken);

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