using System.Text.Json;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Controllers;
using Esi.AI.Studio.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class FlowRoutingServiceTests
{
    [TestMethod]
    public void SelectWorkflowName_ToolRequest_SelectsToolRouter()
    {
        Assert.AreEqual("Tool calling router", FlowRoutingService.SelectWorkflowName(true, true));
    }

    [TestMethod]
    public void SelectWorkflowName_ImageRequest_SelectsVisionRouter()
    {
        Assert.AreEqual("Vision request router", FlowRoutingService.SelectWorkflowName(false, true));
    }

    [TestMethod]
    public void SelectWorkflowName_TextRequest_SelectsChatRouter()
    {
        Assert.AreEqual("Chat request router", FlowRoutingService.SelectWorkflowName(false, false));
    }

    [TestMethod]
    public void ReadRouteTarget_MultipleBackendNodes_ReturnsLastTarget()
    {
        var definition = new FlowDefinition(
            Guid.NewGuid(),
            "Chat request router",
            1,
            true,
            "{\"nodes\":[{\"name\":\"first\",\"detail\":\"first-profile\",\"kind\":\"backend\"},{\"name\":\"last\",\"detail\":\"selected-profile\",\"kind\":\"backend\"}]}",
            DateTime.UtcNow,
            DateTime.UtcNow);

        Assert.AreEqual("selected-profile", FlowRoutingService.ReadRouteTarget(definition));
    }

    [TestMethod]
    public void ReadRouteTarget_PascalCaseProperties_ReturnsBackendTarget()
    {
        var definition = new FlowDefinition(
            Guid.NewGuid(),
            "Chat request router",
            1,
            false,
            "{\"Nodes\":[{\"Name\":\"Backend route\",\"Detail\":\"Loaded local model\",\"Kind\":\"backend\"}]}",
            DateTime.UtcNow,
            DateTime.UtcNow);

        Assert.AreEqual("Loaded local model", FlowRoutingService.ReadRouteTarget(definition));
    }

    [TestMethod]
    public void ReadRouteTarget_NoBackendNode_ThrowsInvalidOperationException()
    {
        var definition = new FlowDefinition(Guid.NewGuid(), "Chat request router", 1, true, "{\"nodes\":[]}", DateTime.UtcNow, DateTime.UtcNow);

        Assert.ThrowsExactly<InvalidOperationException>(() => FlowRoutingService.ReadRouteTarget(definition));
    }

    [TestMethod]
    public async Task FlowRouteAsync_StudioChatRequest_ExecutesPublishedFlowAndPreservesRequestedModel()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"esi-flow-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={Path.Combine(temporaryDirectory, "studio.db")}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(options);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        using var modelRuntime = new ModelRuntime();
        var workflowRuntime = new RecordingOptimajetFlowRuntime();
        var dataService = new DataService(
            dbContextFactory,
            null!,
            null!,
            null!,
            null!,
            new OpenVinoDiagnosticsService(),
            new OpenVinoDriverInstaller(),
            modelRuntime,
            optimajetFlowRuntime: workflowRuntime);
        try
        {
            await dataService.FlowDefinition_SeedDefaultsAsync();
            var result = await dataService.FlowRouteAsync(
                new ChatExchangeRequest("Hello", "/models/request.gguf", "CPU"));

            Assert.AreEqual("Chat request router", workflowRuntime.WorkflowName);
            Assert.AreEqual("Loaded local model", workflowRuntime.RouteTarget);
            Assert.AreEqual("/models/request.gguf", result.ModelIdentifier);
            Assert.IsTrue(result.SelectedConfiguration is null);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FlowRouteAsync_UnpublishedWorkflow_ThrowsInvalidOperationException()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"esi-flow-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={Path.Combine(temporaryDirectory, "studio.db")}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(options);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        using var modelRuntime = new ModelRuntime();
        var dataService = new DataService(
            dbContextFactory,
            null!,
            null!,
            null!,
            null!,
            new OpenVinoDiagnosticsService(),
            new OpenVinoDriverInstaller(),
            modelRuntime,
            optimajetFlowRuntime: new RecordingOptimajetFlowRuntime());

        try
        {
            await dataService.FlowDefinition_SeedDefaultsAsync();
            var chatFlow = (await dataService.FlowDefinition_ReadAsync())
                .Single(definition => definition.Name == "Chat request router");
            await dataService.FlowDefinition_UpdateAsync(chatFlow with { IsPublished = false, Version = 2 });

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => dataService.FlowRouteAsync(new ChatExchangeRequest("Hello", "/models/request.gguf", "CPU")));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FlowRouteAsync_UnsupportedUpstreamTarget_ThrowsInvalidOperationException()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"esi-flow-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={Path.Combine(temporaryDirectory, "studio.db")}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(options);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        using var modelRuntime = new ModelRuntime();
        var dataService = new DataService(
            dbContextFactory,
            null!,
            null!,
            null!,
            null!,
            new OpenVinoDiagnosticsService(),
            new OpenVinoDriverInstaller(),
            modelRuntime,
            optimajetFlowRuntime: new RecordingOptimajetFlowRuntime());
        var now = DateTime.UtcNow;

        try
        {
            var definition = new FlowDefinition(
                Guid.Empty,
                "Chat request router",
                1,
                true,
                "{\"nodes\":[{\"name\":\"Backend route\",\"detail\":\"OmniRoute upstream\",\"kind\":\"backend\"}]}",
                now,
                now);
            await dataService.FlowDefinition_CreateAsync(definition);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => dataService.FlowRouteAsync(new OpenAiChatRequest(null, [new OpenAiChatMessage("user", "Hello")])));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CreateChatCompletion_PublishedFlowSelectsConfiguredProfileBeforeBackendResolution()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"esi-flow-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var modelPath = Path.Combine(temporaryDirectory, "route.gguf");
        await File.WriteAllBytesAsync(modelPath, [0x47, 0x47, 0x55, 0x46]);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={Path.Combine(temporaryDirectory, "studio.db")}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(options);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        using var modelRuntime = new ModelRuntime();
        var workflowRuntime = new RecordingOptimajetFlowRuntime();
        var dataService = new DataService(
            dbContextFactory,
            null!,
            null!,
            null!,
            null!,
            new OpenVinoDiagnosticsService(),
            new OpenVinoDriverInstaller(),
            modelRuntime,
            optimajetFlowRuntime: workflowRuntime);
        var now = DateTime.UtcNow;

        try
        {
            await dataService.FlowDefinition_SeedDefaultsAsync();
            var configuration = await dataService.ModelConfiguration_CreateAsync(new ModelConfiguration(
                Guid.Empty,
                "Flow-selected profile",
                null,
                modelPath,
                false,
                1,
                JsonSerializer.Serialize(new LoadModelRequest(modelPath, "CPU", 0, 4096, new Dictionary<string, float>(), null)),
                now,
                now,
                ConfigurationBackend.Llama,
                AutoLaunch: false,
                BackendVariantId: "llama.cpu"));
            var flow = (await dataService.FlowDefinition_ReadAsync())
                .Single(definition => definition.Name == "Chat request router");
            var routeJson = JsonSerializer.Serialize(new
            {
                nodes = new[]
                {
                    new { name = "Backend route", detail = configuration.Name, kind = "backend" }
                }
            });
            await dataService.FlowDefinition_UpdateAsync(flow with { DefinitionJson = routeJson, Version = 2 });

            var controller = new OpenAiCompatibleController(
                modelRuntime,
                new EmptyLocalModelCatalog(),
                new OpenAiCompatibleBackendMiddleware(modelRuntime, new InferenceScheduler()),
                dataService)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            var result = await controller.CreateChatCompletion(
                new OpenAiChatRequest("unknown-client-model", [new OpenAiChatMessage("user", "Hello")]),
                CancellationToken.None);

            Assert.AreEqual(configuration.Name, workflowRuntime.RouteTarget);
            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ((ObjectResult)result).StatusCode);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class RecordingOptimajetFlowRuntime : IOptimajetFlowRuntime
    {
        public string? WorkflowName { get; private set; }
        public string? RouteTarget { get; private set; }

        public Task<string> ExecuteRouteAsync(FlowDefinition definition, string routeTarget, CancellationToken cancellationToken = default)
        {
            WorkflowName = definition.Name;
            RouteTarget = routeTarget;
            return Task.FromResult(routeTarget);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }

    private sealed class EmptyLocalModelCatalog : ILocalModelCatalog
    {
        public Task<IReadOnlyList<LocalModelInfo>> ScanLocalModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalModelInfo>>([]);
    }
}
