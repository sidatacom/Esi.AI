namespace Esi.AI.LiteLlm;

/// <summary>
/// Standard-Implementierung von <see cref="IChatCompletionService"/>.
/// Verwaltet einen einzelnen Provider und koordiniert Fehler, Tokenisierung und Logik.
/// </summary>
public sealed class ChatCompletionService : IChatCompletionService
{
    private readonly Server.IChatCompletionProvider _provider;
    private readonly ILogger<ChatCompletionService>? _logger;

    public string ProviderName => _provider.Name;

    public ChatCompletionService(Server.IChatCompletionProvider provider, ILogger<ChatCompletionService>? logger = null)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<Esi.AI.LiteLlm.Contracts.ChatCompletionResponse> CompleteAsync(
        Esi.AI.LiteLlm.Client.Contracts.ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _provider.CompleteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return MapToResponse(request.Model, result);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is System.OperationCanceledException)
        {
            _logger?.LogWarning(ex, "CompleteAsync: HTTP-Fehler bei Provider {Provider}", ProviderName);
            return new Esi.AI.LiteLlm.Contracts.ChatCompletionResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                Model = request.Model,
                Choices = Array.Empty<Esi.AI.LiteLlm.Contracts.ChatCompletionResponse.Choice>().AsReadOnly(),
                Error = new Esi.AI.LiteLlm.Contracts.ChatCompletionResponse.ChatError
                {
                    Reason = "Provider HTTP request failed.",
                    Code = "http_error",
                    StatusCode = 503,
                },
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "CompleteAsync: unerwarteter Fehler bei Provider {Provider}", ProviderName);
            return new Esi.AI.LiteLlm.Contracts.ChatCompletionResponse
            {
                Id = Guid.NewGuid().ToString("N"),
                Model = request.Model,
                Choices = Array.Empty<Esi.AI.LiteLlm.Contracts.ChatCompletionResponse.Choice>().AsReadOnly(),
                Error = new Esi.AI.LiteLlm.Contracts.ChatCompletionResponse.ChatError
                {
                    Reason = "Chat completion failed.",
                    Code = "internal_error",
                    StatusCode = 500,
                },
            };
        }
    }

    public async Task<Esi.AI.LiteLlm.FinishingState> CompleteStreamingAsync(
        Esi.AI.LiteLlm.Client.Contracts.ChatCompletionRequest request,
        Action<string, Esi.AI.LiteLlm.ChatTokenMetadata> onToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sb = new System.Text.StringBuilder();

            await foreach (var chunk in _provider.CompleteStreamingAsync(request, cancellationToken)
                .ConfigureAwait(false))
            {
                var text = chunk.Content ?? string.Empty;
                sb.Append(text);
                onToken(text, new Esi.AI.LiteLlm.ChatTokenMetadata(request.Model, DateTime.UtcNow));
            }

            return new Esi.AI.LiteLlm.FinishingState(Esi.AI.LiteLlm.FinishReason.Stopped, sb.Length);
        }
        catch (System.OperationCanceledException)
        {
            _logger?.LogDebug("CompleteStreamingAsync: abgebrochen.");
            return new Esi.AI.LiteLlm.FinishingState(Esi.AI.LiteLlm.FinishReason.Aborted, 0);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "CompleteStreamingAsync: Fehler.");
            return new Esi.AI.LiteLlm.FinishingState(Esi.AI.LiteLlm.FinishReason.Failed, 0);
        }
    }

    private static Esi.AI.LiteLlm.Contracts.ChatCompletionResponse MapToResponse(string model, Server.ChatCompletionResult r) =>
        new()
        {
            Id = r.Id,
            Model = model,
            Choices = new System.Collections.Generic.List<Esi.AI.LiteLlm.Contracts.ChatCompletionResponse.Choice>
            {
                new()
                {
                    Index = 0,
                    FinishReason = r.FinishReason,
                    Message = new Esi.AI.LiteLlm.Client.Contracts.ChatMessage
                    {
                        Role = "assistant",
                        Content = r.Content,
                    }
                }
            }.AsReadOnly(),
            Usage = r.Usage != null
                ? new Esi.AI.LiteLlm.Contracts.ChatCompletionResponse.UsageInfo
                {
                    InputTokens = r.Usage.InputTokens,
                    OutputTokens = r.Usage.OutputTokens,
                }
                : null,
        };
}
