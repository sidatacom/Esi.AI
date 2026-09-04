using System.Text;
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
    public void GetApplicationModels_WhenNoModelLoaded_ReturnsModelLoadStatus()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);

        var result = controller.GetApplicationModels();

        var response = ((OkObjectResult)result).Value as ModelLoadStatus;
        Assert.IsNotNull(response);
        Assert.IsFalse(response.IsModelLoaded);
        Assert.IsEmpty(response.LoadedModels);
    }

    [TestMethod]
    public async Task LoadLlamaModel_WhenRequestBodyIsMissing_ReturnsBadRequest()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);

        var result = await controller.LoadLlamaModel(null, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public async Task LoadConfiguredModel_WhenRequestBodyIsMissing_ReturnsBadRequest()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);

        var result = await controller.LoadConfiguredModel(null, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public async Task UnloadApplicationModel_WhenCallerIsRemote_ReturnsForbidden()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);
        controller.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1");

        var result = await controller.UnloadApplicationModel(
            new ApplicationModelUnloadRequest("/models/model.gguf", ConfigurationBackend.Llama),
            CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
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
            modelLibrary,
            modelLibrary,
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
    public void ToOpenVinoOptions_WhenTemperatureIsZero_DisablesSampling()
    {
        var options = OpenAiCompatibleController.ToOpenVinoOptions(new ChatGenerationOptions(Temperature: 0));

        Assert.IsFalse(options.DoSample);
    }

    [TestMethod]
    public void ToOpenVinoOptions_WhenTemperatureIsPositive_EnablesSampling()
    {
        var options = OpenAiCompatibleController.ToOpenVinoOptions(new ChatGenerationOptions(Temperature: .7f));

        Assert.IsTrue(options.DoSample);
    }

    [TestMethod]
    public void ToOpenVinoOptions_WhenReasoningEffortIsProvided_PreservesIt()
    {
        var options = OpenAiCompatibleController.ToOpenVinoOptions(new ChatGenerationOptions(ReasoningEffort: "high"));

        Assert.AreEqual("high", options.ReasoningEffort);
    }

    [TestMethod]
    public async Task CreateChatCompletion_WhenReasoningEffortIsUnsupported_ReturnsBadRequest()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);
        var request = new OpenAiChatRequest(
            null,
            new[] { new OpenAiChatMessage("user", "Hello") })
        {
            ReasoningEffort = "unsupported"
        };

        var result = await controller.CreateChatCompletion(request, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
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
    public async Task CreateChatCompletion_WhenLocalToolIsRequestedAndNoModelIsLoaded_ReturnsServiceUnavailable()
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

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task CreateChatCompletion_WhenLocalContentIsMultimodalAndNoModelIsLoaded_ReturnsServiceUnavailable()
    {
        using var runtime = new ModelRuntime();
        var controller = CreateController(runtime);
        using var document = JsonDocument.Parse("[{\"type\":\"text\",\"text\":\"Hello\"}]");
        var request = new OpenAiChatRequest(
            null,
            new[] { new OpenAiChatMessage("user", document.RootElement.Clone()) });

        var result = await controller.CreateChatCompletion(request, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public void ParseMessage_WhenLocalImageDataUrlIsProvided_ReturnsDecodedImage()
    {
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(imageData)}";
        using var document = JsonDocument.Parse($"[{{\"type\":\"text\",\"text\":\"Describe\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{dataUrl}\"}}}}]");

        var message = OpenAiCompatibleController.ParseMessage(new OpenAiChatMessage("user", document.RootElement.Clone()));

        Assert.AreEqual("Describe", message.Content);
        Assert.IsNotNull(message.Images);
        Assert.AreEqual("image/png", message.Images[0].MediaType);
        CollectionAssert.AreEqual(imageData, message.Images[0].Data);
    }

    [TestMethod]
    public void MultimodalChat_WhenPictureFileIsProvided_CreatesOpenVinoImageTensor()
    {
        OpenVinoModelLoader.InitializeRuntime();
        var imageData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "test-chat-with-picture.png"));
        var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(imageData)}";
        using var document = JsonDocument.Parse($"[{{\"type\":\"text\",\"text\":\"Describe the image\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{dataUrl}\"}}}}]");

        var message = OpenAiCompatibleController.ParseMessage(new OpenAiChatMessage("user", document.RootElement.Clone()));
        var tensors = OpenVinoImageTensorFactory.Create([message]);

        try
        {
            Assert.AreEqual("Describe the image", message.Content);
            Assert.IsNotNull(message.Images);
            Assert.AreEqual(1, message.Images.Count);
            Assert.AreEqual(1, tensors.Length);
            using var shape = tensors[0].Shape;
            CollectionAssert.AreEqual(new long[] { 1, 240, 495, 3 }, shape.get_dims());
            Assert.AreEqual(240 * 495 * 3, tensors[0].GetData<byte>(240 * 495 * 3).Length);
        }
        finally
        {
            foreach (var tensor in tensors)
                tensor.Dispose();
        }
    }

    [TestMethod]
    [TestCategory("OpenVINO.Integration")]
    public async Task OpenAiCompatibleApi_WhenPictureIsProvided_DescribesVisibleTools()
    {
        var apiUrl = Environment.GetEnvironmentVariable("ESI_STUDIO_API_URL") ?? "http://127.0.0.1:7010";
        using var client = new HttpClient
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(5)
        };

        using var modelsResponse = await client.GetAsync("v1/models");
        var modelsJson = await modelsResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(modelsResponse.IsSuccessStatusCode, modelsJson);
        using var modelsDocument = JsonDocument.Parse(modelsJson);
        var model = modelsDocument.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .FirstOrDefault(item =>
                item.GetProperty("loaded").GetBoolean() &&
                item.GetProperty("capabilities").GetProperty("imageInput").GetBoolean());
        Assert.IsTrue(model.ValueKind is not JsonValueKind.Undefined, "No loaded image-capable model was reported by the Studio WebAPI.");

        var imageData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "test-chat-with-picture.png"));
        var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(imageData)}";
        var request = new
        {
            model = model.GetProperty("id").GetString(),
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Lies die vier sichtbaren Logo-Beschriftungen im Bild von links nach rechts und nenne ihre Namen exakt." },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            },
            stream = false,
            max_tokens = 512,
            temperature = 0
        };

        using var requestContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("v1/chat/completions", requestContent);
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, responseJson);
        using var responseDocument = JsonDocument.Parse(responseJson);
        var answer = responseDocument.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        Assert.IsFalse(string.IsNullOrWhiteSpace(answer), responseJson);
        Assert.IsFalse(answer.Contains("UNKNOWN_EXCEPTION", StringComparison.OrdinalIgnoreCase), responseJson);
    }

    [TestMethod]
    public void ParseMessage_WhenContentIsJsonStringElement_ReturnsText()
    {
        using var document = JsonDocument.Parse("\"Describe this image\"");

        var message = OpenAiCompatibleController.ParseMessage(new OpenAiChatMessage("user", document.RootElement.Clone()));

        Assert.AreEqual("Describe this image", message.Content);
        Assert.IsNull(message.Images);
    }

    [TestMethod]
    public void ParseMessage_WhenRemoteImageUrlIsProvided_ThrowsArgumentException()
    {
        using var document = JsonDocument.Parse("[{\"type\":\"image_url\",\"image_url\":{\"url\":\"https://example.com/image.png\"}}]");

        Assert.Throws<ArgumentException>(() => OpenAiCompatibleController.ParseMessage(new OpenAiChatMessage("user", document.RootElement.Clone())));
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