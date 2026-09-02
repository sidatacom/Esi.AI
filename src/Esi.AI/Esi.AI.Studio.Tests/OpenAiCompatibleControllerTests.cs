using System.Text.Json;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Controllers;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class OpenAiCompatibleControllerTests
{
    [TestMethod]
    public async Task ListModels_WhenNoModelLoaded_ReturnsOpenAiModelList()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);

        var result = await controller.ListModels(CancellationToken.None);
        var response = ((OkObjectResult)result).Value;
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        StringAssert.Contains(json, "\"object\":\"list\"");
        StringAssert.Contains(json, "\"object\":\"list\"");
    }

    [TestMethod]
    public async Task ListModels_WhenLocalModelsExist_ReturnsEveryModelWithDisplayName()
    {
        using var runtime = new ModelRuntime();
        var models = new[]
        {
            new LocalModelInfo("Qwen 3", "/models/qwen3.gguf", 100, DateTime.UtcNow, ReferenceModelFormat.Gguf),
            new LocalModelInfo("SmolLM", "/models/smollm.gguf", 100, DateTime.UtcNow, ReferenceModelFormat.Gguf)
        };
        var controller = CreateController(runtime, new TestLocalModelCatalog(models));

        var result = await controller.ListModels(CancellationToken.None);
        var response = ((OkObjectResult)result).Value as OpenAiModelListResponse;

        Assert.IsNotNull(response);
        Assert.AreEqual(2, response.Data.Count);
        Assert.AreEqual("/models/qwen3.gguf", response.Data[0].Id);
        Assert.AreEqual("Qwen 3", response.Data[0].Name);
        Assert.AreEqual("SmolLM", response.Data[1].Name);
    }

    [TestMethod]
    public async Task ListModels_WhenStoredCapabilitiesExist_PreservesThem()
    {
        var directory = Directory.CreateTempSubdirectory("esi-ai-capabilities-").FullName;
        var modelPath = Path.Combine(directory, "vision.gguf");
        var databasePath = Path.Combine(directory, "test.db");
        await File.WriteAllBytesAsync(modelPath, [0x47, 0x47, 0x55, 0x46]);

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(dbOptions);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        await using var modelLibrary = new ModelLibraryService(
            new HttpClient { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            dbContextFactory,
            Options.Create(new ModelLibraryOptions { Directories = [directory] }));
        var dataService = new DataService(
            dbContextFactory,
            modelLibrary,
            new OpenVinoDiagnosticsService(),
            new OpenVinoDriverInstaller(),
            new ModelRuntime());
        await dataService.LocalModel_UpdateAsync(new ModelCompatibilityUpdate(
            modelPath,
            Capabilities: new ModelCapabilities(ToolCalling: true, ImageInput: true, Thinking: true)));

        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime, dataService: dataService);

        var result = await controller.ListModels(CancellationToken.None);
        var response = ((OkObjectResult)result).Value as OpenAiModelListResponse;

        Assert.IsNotNull(response);
        var model = response.Data.Single(item => item.Id == modelPath);
        Assert.IsTrue(model.Capabilities!.ToolCalling);
        Assert.IsTrue(model.Capabilities.ImageInput);
        Assert.IsTrue(model.Capabilities.Thinking);

        Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    public void OpenAiModel_SerializesAllProviderCapabilities()
    {
        var model = new OpenAiModel(
            "/models/vision.gguf",
            "model",
            1,
            "esi-ai",
            "vision",
            new ModelCapabilities(ToolCalling: true, ImageInput: true, AgentMode: true, Thinking: true));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(model, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var capabilities = document.RootElement.GetProperty("capabilities");

        Assert.IsTrue(capabilities.GetProperty("toolCalling").GetBoolean());
        Assert.IsTrue(capabilities.GetProperty("imageInput").GetBoolean());
        Assert.IsTrue(capabilities.GetProperty("agentMode").GetBoolean());
        Assert.IsTrue(capabilities.GetProperty("thinking").GetBoolean());
    }

    [TestMethod]
    public async Task CreateChatCompletion_WhenMessagesAreEmpty_ReturnsBadRequest()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);

        var result = await controller.CreateChatCompletion(
            new OpenAiChatRequest(null, Array.Empty<OpenAiChatMessage>()),
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public async Task CreateChatCompletion_WhenNoModelIsLoaded_ReturnsServiceUnavailable()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);

        var result = await controller.CreateChatCompletion(
            new OpenAiChatRequest(null, new[] { new OpenAiChatMessage("user", "Hello") }),
            CancellationToken.None);

        var statusCode = ((ObjectResult)result).StatusCode;
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, statusCode);
    }

    [TestMethod]
    public async Task CreateChatCompletion_WhenCommonSamplingOptionsAreProvided_ReachesRuntime()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);
        var request = new OpenAiChatRequest(
            null,
            new[] { new OpenAiChatMessage("user", "Hello") })
        {
            TopK = 12,
            MinP = .05f,
            RepetitionPenalty = 1.1f,
            Seed = 42,
            Stop = ["END"]
        };

        var result = await controller.CreateChatCompletion(request, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task CreateChatCompletion_WhenLocalToolIsRequested_ReturnsUnsupportedRequest()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);
        var request = new OpenAiChatRequest(
            null,
            new[] { new OpenAiChatMessage("user", "Use a tool.") })
        {
            Tools = new[] { new OpenAiToolDefinition("function", new OpenAiToolFunction("lookup")) }
        };

        var result = await controller.CreateChatCompletion(request, CancellationToken.None);

        var error = ((BadRequestObjectResult)result).Value as OpenAiErrorResponse;
        Assert.IsNotNull(error);
        Assert.AreEqual("unsupported_request_error", error.Error.Type);
    }

    [TestMethod]
    public async Task CreateChatCompletion_WhenLocalContentIsMultimodal_ReturnsUnsupportedContentError()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);
        using var document = JsonDocument.Parse("[{\"type\":\"text\",\"text\":\"Hello\"}]");
        var request = new OpenAiChatRequest(
            null,
            new[] { new OpenAiChatMessage("user", document.RootElement.Clone()) });

        var result = await controller.CreateChatCompletion(request, CancellationToken.None);

        var error = ((BadRequestObjectResult)result).Value as OpenAiErrorResponse;
        Assert.IsNotNull(error);
        Assert.AreEqual("invalid_request_error", error.Error.Type);
    }

    private static OpenAiCompatibleController CreateController(
        ModelRuntime runtime,
        ILocalModelCatalog? catalog = null,
        DataService? dataService = null)
    {
        var controller = new OpenAiCompatibleController(runtime, catalog ?? new EmptyLocalModelCatalog(), new DisabledOmniRouteClient(), Options.Create(new OmniRouteOptions()), dataService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }

    private sealed class EmptyLocalModelCatalog : ILocalModelCatalog
    {
        public Task<IReadOnlyList<LocalModelInfo>> ScanLocalModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalModelInfo>>([]);
    }

    private sealed class TestLocalModelCatalog(IReadOnlyList<LocalModelInfo> models) : ILocalModelCatalog
    {
        public Task<IReadOnlyList<LocalModelInfo>> ScanLocalModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(models);
    }

    private sealed class DisabledOmniRouteClient : IOmniRouteClient
    {
        public Task<OmniRouteModelsResult> ListModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OmniRouteModelsResult(false, null));

        public Task<HttpResponseMessage> CreateChatCompletionAsync(
            OpenAiChatRequest request,
            string? authorizationHeader,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}