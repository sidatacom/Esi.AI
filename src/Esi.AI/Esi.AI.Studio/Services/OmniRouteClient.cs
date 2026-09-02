using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Esi.AI.Models;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Services;

/// <summary>Forwards OpenAI-compatible requests to an independently hosted OmniRoute instance.</summary>
public sealed class OmniRouteClient : IOmniRouteClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly OmniRouteOptions options;

    /// <summary>Initializes a new instance of the <see cref="OmniRouteClient"/> class.</summary>
    /// <param name="httpClient">The configured OmniRoute HTTP client.</param>
    /// <param name="options">The OmniRoute configuration.</param>
    public OmniRouteClient(HttpClient httpClient, IOptions<OmniRouteOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    /// <inheritdoc />
    public async Task<OmniRouteModelsResult> ListModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("models", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new OmniRouteModelsResult(false, null, (int)response.StatusCode);

            var models = await response.Content.ReadFromJsonAsync<OpenAiModelListResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return models is null
                ? new OmniRouteModelsResult(false, null)
                : new OmniRouteModelsResult(true, models, (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return new OmniRouteModelsResult(false, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OmniRouteModelsResult(false, null, StatusCodes.Status504GatewayTimeout);
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> CreateChatCompletionAsync(
        OpenAiChatRequest request,
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        AddAuthorizationHeader(httpRequest, authorizationHeader);
        return SendAsync(httpRequest, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private void AddAuthorizationHeader(HttpRequestMessage request, string? authorizationHeader)
    {
        if (options.ForwardAuthorizationHeader &&
            AuthenticationHeaderValue.TryParse(authorizationHeader, out var callerAuthorization) &&
            callerAuthorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = callerAuthorization;
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    }
}