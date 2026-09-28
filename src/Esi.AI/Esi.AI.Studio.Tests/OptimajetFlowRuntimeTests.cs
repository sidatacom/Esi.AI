using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class OptimajetFlowRuntimeTests
{
    [TestMethod]
    public async Task ExecuteRouteAsync_PublishedDefinition_ReturnsConfiguredTarget()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"esi-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(temporaryDirectory, "workflow.db")}" 
            })
            .Build();
        var runtime = new OptimajetFlowRuntime(configuration);
        var started = false;

        try
        {
            await runtime.StartAsync(CancellationToken.None);
            started = true;

            var result = await runtime.ExecuteRouteAsync(
                new FlowDefinition(
                    Guid.NewGuid(),
                    "Chat request router",
                    1,
                    true,
                    "{}",
                    DateTime.UtcNow,
                    DateTime.UtcNow),
                "default-model");

            Assert.AreEqual("default-model", result);
        }
        finally
        {
            if (started)
                await runtime.StopAsync(CancellationToken.None);

            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExecuteRouteAsync_UnpublishedDefinition_ThrowsInvalidOperationException()
    {
        var configuration = new ConfigurationBuilder().Build();
        var runtime = new OptimajetFlowRuntime(configuration);
        var definition = new FlowDefinition(Guid.NewGuid(), "Chat request router", 1, false, "{}", DateTime.UtcNow, DateTime.UtcNow);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => runtime.ExecuteRouteAsync(definition, "default-model"));
    }
}