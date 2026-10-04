using System.Text.Json;
using System.Text.Json.Nodes;

namespace Esi.AI.Workflow;

/// <summary>
/// Converts the compact Esi.AI route document to the Elsa flowchart document
/// consumed by the open-source Elsa designer.
/// </summary>
public static class ElsaFlowchartAdapter
{
    private const string EsiNodeType = "Esi.AI.Workflow.Node";

    /// <summary>
    /// Converts an Esi.AI definition into an Elsa-compatible flowchart.
    /// </summary>
    public static JsonObject ToElsaFlowchart(string? definitionJson)
    {
        var source = ParseObject(definitionJson);
        if (source["elsaFlowchart"] is JsonObject storedFlowchart)
        {
            var flowchart = (JsonObject)storedFlowchart.DeepClone();
            foreach (var activity in flowchart["activities"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                if (activity["designerMetadata"] is not JsonObject legacyDesignerMetadata)
                    continue;

                var metadata = activity["metadata"] as JsonObject;
                if (metadata is null)
                {
                    metadata = new JsonObject();
                    activity["metadata"] = metadata;
                }

                if (metadata["designer"] is null)
                    metadata["designer"] = legacyDesignerMetadata.DeepClone();
                activity.Remove("designerMetadata");
            }

            return flowchart;
        }

        var nodes = source["nodes"] as JsonArray ?? [];
        var activities = new JsonArray();
        var connections = new JsonArray();
        var activityIds = new List<string>();

        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is not JsonObject node)
                continue;

            var id = $"esi-node-{index + 1}";
            activityIds.Add(id);
            activities.Add(new JsonObject
            {
                ["id"] = id,
                ["nodeId"] = id,
                ["type"] = EsiNodeType,
                ["version"] = 1,
                ["name"] = ReadString(node, "name") ?? $"Node {index + 1}",
                ["routeTarget"] = ReadString(node, "detail") ?? string.Empty,
                ["kind"] = ReadString(node, "kind") ?? "route",
                ["metadata"] = new JsonObject
                {
                    ["designer"] = new JsonObject
                    {
                        ["position"] = new JsonObject { ["x"] = 80 + index * 275, ["y"] = 140 },
                        ["size"] = new JsonObject { ["width"] = 220, ["height"] = 90 }
                    }
                }
            });

        }

        for (var index = 1; index < activityIds.Count; index++)
            connections.Add(new JsonObject
            {
                ["source"] = new JsonObject { ["activity"] = activityIds[index - 1], ["port"] = "Done" },
                ["target"] = new JsonObject { ["activity"] = activityIds[index], ["port"] = "In" },
                ["vertices"] = new JsonArray()
            });

        return new JsonObject
        {
            ["id"] = "esi-flow",
            ["nodeId"] = "esi-flow",
            ["type"] = "Elsa.Flowchart",
            ["version"] = 1,
            ["name"] = source["name"]?.GetValue<string>() ?? "Esi.AI Flow",
            ["activities"] = activities,
            ["connections"] = connections
        };
    }

    /// <summary>
    /// Converts an Elsa flowchart back to the compact Esi.AI route document.
    /// </summary>
    public static string FromElsaFlowchart(JsonObject flowchart, string? originalDefinitionJson)
    {
        var original = ParseObject(originalDefinitionJson);
        var activities = flowchart["activities"] as JsonArray ?? [];
        var nodes = new JsonArray();

        foreach (var activity in activities.OfType<JsonObject>())
        {
            nodes.Add(new JsonObject
            {
                ["name"] = ReadString(activity, "name") ?? "Workflow node",
                ["detail"] = ReadString(activity, "routeTarget") ?? ReadString(activity, "displayName") ?? string.Empty,
                ["kind"] = ReadString(activity, "kind") ?? "route"
            });
        }

        original["elsaFlowchart"] = flowchart.DeepClone();
        original["nodes"] = nodes;
        return original.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string? ReadString(JsonObject source, string propertyName)
    {
        var value = source.FirstOrDefault(property => string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Value;
        return value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : null;
    }

    private static JsonObject ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
}
