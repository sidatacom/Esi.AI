using System.Text.Json;

namespace Esi.AI.Models;

/// <summary>Reads backend routing information from a persisted flow definition.</summary>
public static class FlowDefinitionRouteParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Returns the last backend target in a flow definition.</summary>
    /// <param name="definitionJson">The persisted workflow JSON.</param>
    /// <param name="workflowName">The workflow name used in validation errors.</param>
    /// <returns>The non-empty backend target.</returns>
    /// <exception cref="ArgumentException">The workflow name or JSON is empty.</exception>
    /// <exception cref="InvalidOperationException">The workflow has no backend route.</exception>
    public static string ReadBackendTarget(string definitionJson, string workflowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        var document = JsonSerializer.Deserialize<FlowDocument>(definitionJson, SerializerOptions);
        var backendNode = document?.Nodes?.LastOrDefault(node =>
            string.Equals(node.Kind, "backend", StringComparison.OrdinalIgnoreCase));
        if (backendNode is null || string.IsNullOrWhiteSpace(backendNode.Detail))
            throw new InvalidOperationException($"The workflow '{workflowName}' has no backend route.");

        return backendNode.Detail.Trim();
    }

    private sealed record FlowDocument(FlowNode[]? Nodes);

    private sealed record FlowNode(string? Name, string? Detail, string? Kind);
}