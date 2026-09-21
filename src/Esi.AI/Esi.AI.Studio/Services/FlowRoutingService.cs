using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Resolves an incoming API request to one persisted workflow definition.</summary>
public sealed class FlowRoutingService(DataService dataService)
{
    public async Task<FlowRoutingDecision> RouteAsync(OpenAiChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workflowName = request.Tools is { Count: > 0 }
            ? "Tool calling router"
            : ContainsImage(request.Messages)
                ? "Vision request router"
                : "Chat request router";
        var definition = (await dataService.FlowDefinition_ReadAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Name, workflowName, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            throw new InvalidOperationException($"The workflow '{workflowName}' is not configured.");

        var document = JsonSerializer.Deserialize<FlowDocument>(definition.DefinitionJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        var backendNode = document?.Nodes?.LastOrDefault(node => string.Equals(node.Kind, "backend", StringComparison.OrdinalIgnoreCase));
        if (backendNode is null || string.IsNullOrWhiteSpace(backendNode.Detail))
            throw new InvalidOperationException($"The workflow '{workflowName}' has no backend route.");

        var routeTarget = backendNode.Detail.Trim();
        if (string.Equals(routeTarget, "OmniRoute upstream", StringComparison.OrdinalIgnoreCase))
            return new FlowRoutingDecision(definition.Name, definition.Version, routeTarget, null, true);

        var configurations = await dataService.ModelConfiguration_ReadAsync(cancellationToken).ConfigureAwait(false);
        var selectedConfiguration = configurations.FirstOrDefault(configuration =>
            string.Equals(configuration.Name, routeTarget, StringComparison.OrdinalIgnoreCase));
        if (selectedConfiguration is null && string.Equals(routeTarget, "Vision backend", StringComparison.OrdinalIgnoreCase))
        {
            var localModels = await dataService.LocalModel_ReadAsync(cancellationToken).ConfigureAwait(false);
            var visionPath = localModels.FirstOrDefault(model => model.Capabilities?.ImageInput == true)?.Path;
            selectedConfiguration = configurations.FirstOrDefault(configuration =>
                string.Equals(configuration.ModelPath, visionPath, StringComparison.OrdinalIgnoreCase));
        }

        var isDefaultLocalRoute = routeTarget.Equals("Loaded local model", StringComparison.OrdinalIgnoreCase) ||
            routeTarget.Equals("Multimodal local model", StringComparison.OrdinalIgnoreCase) ||
            routeTarget.Equals("Local chat backend", StringComparison.OrdinalIgnoreCase);
        if (selectedConfiguration is null && !isDefaultLocalRoute)
            throw new InvalidOperationException($"The workflow '{workflowName}' references an unavailable backend route '{routeTarget}'.");

        return new FlowRoutingDecision(definition.Name, definition.Version, routeTarget, selectedConfiguration?.Name, false);
    }

    private static bool ContainsImage(IReadOnlyList<OpenAiChatMessage>? messages) =>
        messages?.Any(message => message.Content is JsonElement { ValueKind: JsonValueKind.Array } parts &&
            parts.EnumerateArray().Any(part =>
                part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "image_url", StringComparison.OrdinalIgnoreCase))) == true;

    private sealed record FlowDocument(FlowNode[]? Nodes);

    private sealed record FlowNode(string Name, string Detail, string Kind);
}

public sealed record FlowRoutingDecision(
    string WorkflowName,
    int Version,
    string RouteTarget,
    string? ModelIdentifier,
    bool UseOmniRoute);