using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Classifies requests and reads backend targets from published flow definitions.</summary>
public static class FlowRoutingService
{
    /// <summary>Returns the named flow that handles a chat request.</summary>
    public static string SelectWorkflowName(bool hasTools, bool hasImages) => hasTools
        ? "Tool calling router"
        : hasImages
            ? "Vision request router"
            : "Chat request router";

    /// <summary>Returns the last backend node target in a flow definition.</summary>
    public static string ReadRouteTarget(FlowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return FlowDefinitionRouteParser.ReadBackendTarget(definition.DefinitionJson, definition.Name);
    }

    /// <summary>Determines whether an OpenAI chat request contains an image part.</summary>
    public static bool ContainsImage(IReadOnlyList<OpenAiChatMessage>? messages) =>
        messages?.Any(message => message.Content is JsonElement { ValueKind: JsonValueKind.Array } parts &&
            parts.EnumerateArray().Any(part =>
                part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "image_url", StringComparison.OrdinalIgnoreCase))) == true;

}

/// <summary>Describes the flow and backend configuration selected for an incoming request.</summary>
public sealed record FlowRoutingDecision(
    string WorkflowName,
    int Version,
    string RouteTarget,
    string? ModelIdentifier,
    ModelConfiguration? SelectedConfiguration = null);