using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Provides access to the optional OmniRoute OpenAI-compatible upstream.</summary>
public interface IOmniRouteClient
{
    /// <summary>Reads the models exposed by OmniRoute.</summary>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The upstream model-list result.</returns>
    Task<OmniRouteModelsResult> ListModelsAsync(CancellationToken cancellationToken);

    /// <summary>Creates a chat completion request at OmniRoute.</summary>
    /// <param name="request">The OpenAI-compatible chat request.</param>
    /// <param name="authorizationHeader">The optional caller authorization header.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The upstream HTTP response, whose content remains streamable.</returns>
    Task<HttpResponseMessage> CreateChatCompletionAsync(
        OpenAiChatRequest request,
        string? authorizationHeader,
        CancellationToken cancellationToken);
}

/// <summary>Contains a parsed OmniRoute model-list response or its upstream status.</summary>
public sealed record OmniRouteModelsResult(
    bool Succeeded,
    OpenAiModelListResponse? Models,
    int StatusCode = StatusCodes.Status503ServiceUnavailable);