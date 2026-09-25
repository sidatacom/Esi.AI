using System.Text.Json.Nodes;
using Esi.AI.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class ElsaFlowchartAdapterTests
{
    [TestMethod]
    public void FromElsaFlowchart_PreservesElsaDocumentAndProjectsRouteNodes()
    {
        var flowchart = JsonNode.Parse("""
            {
              "id": "root-flow",
              "type": "Elsa.Flowchart",
              "activities": [
                {
                  "id": "request-node",
                  "nodeId": "root-flow:request-node",
                  "type": "Esi.AI.Workflow.Node",
                  "version": 1,
                  "name": "Route chat",
                  "kind": "backend",
                  "routeTarget": "Loaded local model",
                  "designerMetadata": {
                    "position": { "x": 340, "y": 180 },
                    "size": { "width": 220, "height": 90 }
                  }
                }
              ],
              "connections": [
                {
                  "source": { "activityId": "request-node", "port": "Done" },
                  "target": { "activityId": "next-node", "port": "In" },
                  "vertices": [{ "x": 500, "y": 225 }]
                }
              ],
              "customMetadata": { "owner": "Esi.AI" }
            }
            """)!.AsObject();

        var definitionJson = ElsaFlowchartAdapter.FromElsaFlowchart(flowchart, "{\"description\":\"kept\"}");
        var savedDefinition = JsonNode.Parse(definitionJson)!.AsObject();
        var projectedNode = savedDefinition["nodes"]![0]!.AsObject();
        var restoredFlowchart = ElsaFlowchartAdapter.ToElsaFlowchart(definitionJson);

        Assert.IsTrue(JsonNode.DeepEquals(flowchart, restoredFlowchart));
        Assert.AreEqual("kept", savedDefinition["description"]!.GetValue<string>());
        Assert.AreEqual("Route chat", projectedNode["name"]!.GetValue<string>());
        Assert.AreEqual("Loaded local model", projectedNode["detail"]!.GetValue<string>());
        Assert.AreEqual("backend", projectedNode["kind"]!.GetValue<string>());
    }

    [TestMethod]
    public void ToElsaFlowchart_LegacyRouteNodes_CreatesSequentialActivitiesAndConnections()
    {
        var flowchart = ElsaFlowchartAdapter.ToElsaFlowchart("""
            {
              "nodes": [
                { "name": "Receive request", "detail": "Chat request", "kind": "trigger" },
                { "name": "Select backend", "detail": "Loaded local model", "kind": "backend" }
              ]
            }
            """);

        var activities = flowchart["activities"]!.AsArray();
        var connections = flowchart["connections"]!.AsArray();

        Assert.HasCount(2, activities);
        Assert.HasCount(1, connections);
        Assert.AreEqual("Chat request", activities[0]!["routeTarget"]!.GetValue<string>());
        Assert.AreEqual("Loaded local model", activities[1]!["routeTarget"]!.GetValue<string>());
        Assert.AreEqual("esi-node-1", connections[0]!["source"]!["activityId"]!.GetValue<string>());
        Assert.AreEqual("esi-node-2", connections[0]!["target"]!["activityId"]!.GetValue<string>());
    }
}