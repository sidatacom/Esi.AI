using System.Text.Json.Nodes;
using Elsa.Api.Client.Extensions;
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
                  "metadata": {
                    "designer": {
                      "position": { "x": 340, "y": 180 },
                      "size": { "width": 220, "height": 90 }
                    }
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
    public void ToElsaFlowchart_StoredLegacyDesignerMetadata_MigratesToElsaSchema()
    {
        var flowchart = ElsaFlowchartAdapter.ToElsaFlowchart("""
            {
              "elsaFlowchart": {
                "activities": [
                  {
                    "id": "esi-node-1",
                    "designerMetadata": {
                      "position": { "x": 420, "y": 180 },
                      "size": { "width": 220, "height": 90 }
                    }
                  }
                ]
              }
            }
            """);

        var activity = flowchart["activities"]![0]!.AsObject();

        Assert.AreEqual(420, activity["metadata"]!["designer"]!["position"]!["x"]!.GetValue<int>());
        Assert.IsNull(activity["designerMetadata"]);
    }

    [TestMethod]
    public void ToElsaFlowchart_LegacyRouteNodes_CreatesSequentialActivitiesAndConnections()
    {
        var flowchart = ElsaFlowchartAdapter.ToElsaFlowchart("""
            {
              "nodes": [
                { "Name": "Receive request", "Detail": "Chat request", "Kind": "trigger" },
                { "Name": "Select backend", "Detail": "Loaded local model", "Kind": "backend" }
              ]
            }
            """);

        var activities = flowchart["activities"]!.AsArray();
        var connections = flowchart["connections"]!.AsArray();
        var mappedConnection = flowchart.GetConnections().Single();

        Assert.HasCount(2, activities);
        Assert.HasCount(1, connections);
        Assert.AreEqual("Receive request", activities[0]!["name"]!.GetValue<string>());
        Assert.AreEqual("Chat request", activities[0]!["routeTarget"]!.GetValue<string>());
        Assert.AreEqual("Loaded local model", activities[1]!["routeTarget"]!.GetValue<string>());
        Assert.AreEqual(80, activities[0]!["metadata"]!["designer"]!["position"]!["x"]!.GetValue<int>());
        Assert.AreEqual(355, activities[1]!["metadata"]!["designer"]!["position"]!["x"]!.GetValue<int>());
        Assert.AreEqual("esi-node-1", mappedConnection.Source.ActivityId);
        Assert.AreEqual("Done", mappedConnection.Source.Port);
        Assert.AreEqual("esi-node-2", mappedConnection.Target.ActivityId);
        Assert.AreEqual("In", mappedConnection.Target.Port);
    }
}