using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Esi.AI.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.E2E.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BackendReferenceChatEndToEndTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public TestContext TestContext { get; set; }

    [TestMethod]
    public void ResolveConfigurationTemplate_WhenXpuCandidatesIncludeMixedRoutes_SelectsXpuOnlyConfiguration()
    {
        const string modelPath = "/models/reference";
        var timestamp = DateTime.UtcNow;
        var catalog = new ApplicationModelCatalog([], [
            CreateXpuConfigurationTemplate("E2E vllm.xpu orphan", modelPath, "xpu:0", ["xpu:0"], timestamp),
            CreateXpuConfigurationTemplate("Mixed routes", modelPath, "xpu:1", ["xpu:1", "cuda:0"], timestamp),
            CreateXpuConfigurationTemplate("XPU only", modelPath, "xpu:0", ["xpu:0"], timestamp)
        ]);

        var configuration = ResolveConfigurationTemplate(catalog, ConfigurationBackend.Vllm, "vllm.xpu", modelPath);

        Assert.AreEqual("XPU only", configuration.Name);
    }

    [TestMethod]
    public void PrepareConfigurationTemplateForReferenceModel_WhenVllmTemplateHasModelSpecificOptions_ResetsThem()
    {
        const string modelPath = "/models/reference";
        var timestamp = DateTime.UtcNow;
        var template = CreateXpuConfigurationTemplate("XPU template", "/models/large-gptq", "xpu:0", ["xpu:0"], timestamp);
        var loadRequest = new PythonInferenceLoadRequest(
            "/models/large-gptq",
            ConfigurationBackend.Vllm,
            PythonExecutable: "/venv/bin/python",
            GpuMemoryUtilization: 88,
            MaxModelLength: 131072,
            Device: "xpu:0",
            Devices: ["xpu:0"],
            Quantization: "gptq",
            DType: "float16",
            KvCacheDType: "fp8",
            SpeculativeConfigJson: "{\"method\":\"qwen3_5_mtp\"}",
            MaxNumSeqs: 1,
            MaxNumBatchedTokens: 8192,
            EnableXpuGraph: true,
            EnableBf16MtpDraft: true);
        Assert.AreEqual("xpu:0", loadRequest.Device);
        template = template with { ConfigurationJson = JsonSerializer.Serialize(loadRequest, JsonOptions) };
        var serializedRequest = JsonSerializer.Deserialize<PythonInferenceLoadRequest>(template.ConfigurationJson, JsonOptions);
        Assert.IsNotNull(serializedRequest);
        Assert.AreEqual("xpu:0", serializedRequest.Device);

        var prepared = PrepareConfigurationTemplateForReferenceModel(template, modelPath);
        var preparedRequest = JsonSerializer.Deserialize<PythonInferenceLoadRequest>(prepared.ConfigurationJson, JsonOptions);

        Assert.IsNotNull(preparedRequest);
        Assert.AreEqual(modelPath, preparedRequest.ModelPath);
        Assert.AreEqual("/venv/bin/python", preparedRequest.PythonExecutable);
        Assert.AreEqual("xpu:0", preparedRequest.Device);
        Assert.AreEqual(2048u, preparedRequest.MaxModelLength);
        Assert.IsNull(preparedRequest.GpuMemoryUtilization);
        Assert.AreEqual(string.Empty, preparedRequest.Quantization);
        Assert.AreEqual(string.Empty, preparedRequest.DType);
        Assert.AreEqual(string.Empty, preparedRequest.KvCacheDType);
        Assert.AreEqual(string.Empty, preparedRequest.SpeculativeConfigJson);
        Assert.IsFalse(preparedRequest.EnableXpuGraph);
        Assert.IsFalse(preparedRequest.EnableBf16MtpDraft);
    }

    [TestMethod]
    [TestCategory("EndToEnd")]
    [DataRow(ConfigurationBackend.Llama, "llama.vulkan")]
    [DataRow(ConfigurationBackend.Llama, "llama.cuda12")]
    [DataRow(ConfigurationBackend.Llama, "llama.sycl")]
    [DataRow(ConfigurationBackend.OpenVino, "openvino")]
    [DataRow(ConfigurationBackend.Vllm, "vllm.cuda12")]
    [DataRow(ConfigurationBackend.Vllm, "vllm.xpu")]
    public async Task ReferenceModel_WebApiAndBrowserChat_ReturnNonEmptyAnswers(
        ConfigurationBackend backend,
        string backendVariantId)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ESI_BACKEND_CHAT_E2E"), "1", StringComparison.Ordinal))
            Assert.Inconclusive("Set ESI_BACKEND_CHAT_E2E=1 to run real-model browser and WebAPI chat tests.");

        var configuredBaseAddress = Environment.GetEnvironmentVariable("ESI_STUDIO_BASE_URL") ?? "http://127.0.0.1:7010";
        if (!Uri.TryCreate(configuredBaseAddress, UriKind.Absolute, out var parsedBaseAddress) || parsedBaseAddress is null)
            Assert.Fail("ESI_STUDIO_BASE_URL must be an absolute URL for the running local Studio host.");
        if (!parsedBaseAddress.IsLoopback)
            Assert.Fail("Backend chat E2E tests may only connect to a loopback Studio host.");

        var referenceModel = BackendReferenceModels.All.Single(item => item.Backend == backend);
        var baseAddress = new Uri(parsedBaseAddress.AbsoluteUri.TrimEnd('/') + "/");
        using var httpClient = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromMinutes(5)
        };

        var catalog = await GetJsonAsync<ApplicationModelCatalog>(httpClient, "v1/application/models/catalog");
        var model = ResolveReferenceModel(catalog, referenceModel);
        var modelPath = model.Path;

        await using var hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, "/hubs/data"))
            .Build();
        await hubConnection.StartAsync();

        var configurationTemplate = PrepareConfigurationTemplateForReferenceModel(
            ResolveConfigurationTemplate(catalog, backend, backendVariantId, modelPath),
            modelPath);
        var now = DateTime.UtcNow;
        var configuration = await hubConnection.InvokeAsync<ModelConfiguration>(
            "ModelConfiguration_Create",
            configurationTemplate with
            {
                Id = Guid.Empty,
                Name = $"E2E {backendVariantId} {Guid.NewGuid():N}",
                Description = $"Temporary reference-model chat test for {backendVariantId}.",
                ModelPath = modelPath,
                IsDefault = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                AutoLaunch = false
            });

        var loadAttempted = false;
        try
        {
            var status = await GetJsonAsync<ModelLoadStatus>(httpClient, "v1/application/models");
            var loadedModel = FindLoadedModel(status, backend, backendVariantId, modelPath);
            if (loadedModel is null)
            {
                loadAttempted = true;
                using var loadResponse = await httpClient.PostAsJsonAsync(
                    "v1/application/models/load",
                    new ApplicationModelLoadRequest(model.Id, configuration.Id),
                    JsonOptions);
                var loadBody = await loadResponse.Content.ReadAsStringAsync();
                Assert.IsTrue(loadResponse.IsSuccessStatusCode, $"Loading {backendVariantId} failed: {loadBody}");
                var loadedStatus = JsonSerializer.Deserialize<ModelLoadStatus>(loadBody, JsonOptions)
                    ?? throw new InvalidOperationException($"Loading {backendVariantId} returned an empty response.");
                loadedModel = FindLoadedModel(loadedStatus, backend, backendVariantId, modelPath);
                Assert.IsNotNull(loadedModel, $"Loading {backendVariantId} did not report the reference model as loaded.");
            }

            var models = await GetJsonAsync<OpenAiModelListResponse>(httpClient, "v1/models");
            var apiModel = models.Data.SingleOrDefault(item => item.ConfigurationId == configuration.Id);
            Assert.IsNotNull(apiModel, $"The model catalog did not expose configuration {configuration.Id} through /v1/models.");

            await AssertWebApiChatAsync(httpClient, apiModel.Id, backendVariantId);
            await AssertBrowserChatAsync(baseAddress, backend, backendVariantId, modelPath, GetExpectedChatBackend(backend, backendVariantId));
        }
        finally
        {
            if (loadAttempted)
                await TryUnloadModelAsync(httpClient, backend, backendVariantId, modelPath);

            try
            {
                await hubConnection.InvokeAsync("ModelConfiguration_Delete", configuration.Id);
            }
            catch (Exception exception)
            {
                TestContext.WriteLine($"Could not delete temporary {backendVariantId} configuration: {exception.Message}");
            }
        }
    }

    private static Model ResolveReferenceModel(ApplicationModelCatalog catalog, BackendReferenceModel referenceModel)
    {
        var configuredModelPath = Environment.GetEnvironmentVariable(referenceModel.EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredModelPath) &&
            !string.Equals(configuredModelPath, referenceModel.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            var configuredModel = catalog.Models.SingleOrDefault(model => PathsMatch(model.Path, configuredModelPath));
            if (configuredModel is null)
                Assert.Fail($"{referenceModel.EnvironmentVariable}='{configuredModelPath}' is not present in the Studio model catalog.");
            return configuredModel;
        }

        var modelName = referenceModel.ModelId[(referenceModel.ModelId.LastIndexOf('/') + 1)..];
        var candidates = catalog.Models
            .Where(model => ModelMatchesFormat(model.Path, referenceModel.Format))
            .Where(model => Path.GetFileName(model.Path).Contains(modelName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length != 1)
            Assert.Fail($"Expected one local {referenceModel.Format} reference model matching '{modelName}', found {candidates.Length}. Set {referenceModel.EnvironmentVariable} to a catalog path to select it.");
        return candidates[0];
    }

    private static bool ModelMatchesFormat(string modelPath, ReferenceModelFormat format) => format switch
    {
        ReferenceModelFormat.Gguf => string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase),
        ReferenceModelFormat.OpenVinoIr => Directory.Exists(modelPath) &&
            (File.Exists(Path.Combine(modelPath, "openvino_model.xml")) || File.Exists(Path.Combine(modelPath, "openvino_language_model.xml"))),
        ReferenceModelFormat.Transformers => Directory.Exists(modelPath) &&
            File.Exists(Path.Combine(modelPath, "config.json")) &&
            Directory.EnumerateFiles(modelPath, "*.safetensors", SearchOption.AllDirectories).Any(),
        _ => false
    };

    private static ModelConfiguration ResolveConfigurationTemplate(
        ApplicationModelCatalog catalog,
        ConfigurationBackend backend,
        string backendVariantId,
        string modelPath)
    {
        var template = catalog.Configurations
            .Where(configuration => configuration.Backend == backend &&
                string.Equals(configuration.BackendVariantId, backendVariantId, StringComparison.OrdinalIgnoreCase) &&
                !configuration.Name.StartsWith("E2E ", StringComparison.OrdinalIgnoreCase) &&
                IsConfigurationTemplateCompatible(configuration, backendVariantId))
            .OrderByDescending(configuration => PathsMatch(configuration.ModelPath, modelPath))
            .FirstOrDefault();
        if (template is null)
        {
            var routeRequirement = backendVariantId switch
            {
                "vllm.xpu" => " with only xpu:<index> device routes",
                "vllm.cuda12" => " with CUDA device routes",
                _ => string.Empty
            };
            Assert.Fail($"Create one saved Studio configuration for {backendVariantId}{routeRequirement}; the E2E test clones its backend-specific load settings.");
        }
        return template;
    }

    private static ModelConfiguration PrepareConfigurationTemplateForReferenceModel(
        ModelConfiguration template,
        string modelPath)
    {
        if (template.Backend != ConfigurationBackend.Vllm)
            return template with { ModelPath = modelPath };

        var loadRequest = JsonSerializer.Deserialize<PythonInferenceLoadRequest>(template.ConfigurationJson, JsonOptions)
            ?? throw new InvalidOperationException($"The {template.BackendVariantId} configuration template is empty.");
        return template with
        {
            ModelPath = modelPath,
            ConfigurationJson = JsonSerializer.Serialize(loadRequest with
            {
                ModelPath = modelPath,
                Backend = ConfigurationBackend.Vllm,
                GpuMemoryUtilization = null,
                MaxModelLength = 2048,
                TensorParallelSize = 1,
                StartupTimeout = TimeSpan.FromMinutes(10),
                MaxTokens = 32,
                Temperature = 0,
                TopP = 1,
                EnforceEager = false,
                Quantization = string.Empty,
                DType = string.Empty,
                KvCacheDType = string.Empty,
                SpeculativeConfigJson = string.Empty,
                MaxNumSeqs = 1,
                MaxNumBatchedTokens = 0,
                EnablePrefixCaching = false,
                EnableXpuGraph = false,
                EnableBf16MtpDraft = false
            }, JsonOptions)
        };
    }

    private static ModelConfiguration CreateXpuConfigurationTemplate(
        string name,
        string modelPath,
        string device,
        IReadOnlyList<string> devices,
        DateTime timestamp)
    {
        var configurationJson = JsonSerializer.Serialize(
            new PythonInferenceLoadRequest(modelPath, ConfigurationBackend.Vllm, Device: device, Devices: devices),
            JsonOptions);
        return new ModelConfiguration(
            Guid.NewGuid(),
            name,
            null,
            modelPath,
            false,
            1,
            configurationJson,
            timestamp,
            timestamp,
            ConfigurationBackend.Vllm,
            BackendVariantId: "vllm.xpu");
    }

    private static bool IsConfigurationTemplateCompatible(ModelConfiguration configuration, string backendVariantId)
    {
        if (!string.Equals(backendVariantId, "vllm.xpu", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backendVariantId, "vllm.cuda12", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var loadRequest = JsonSerializer.Deserialize<PythonInferenceLoadRequest>(configuration.ConfigurationJson, JsonOptions);
            if (loadRequest is null)
                return false;

            var configuredDevices = loadRequest.Devices?.Where(device => !string.IsNullOrWhiteSpace(device)).ToArray() ?? [];
            var devices = configuredDevices.Length > 0 ? configuredDevices : [loadRequest.Device];
            if (string.Equals(backendVariantId, "vllm.cuda12", StringComparison.OrdinalIgnoreCase))
                return devices.Length > 0 && devices.All(IsCudaDeviceRoute);

            return devices.All(IsXpuDeviceRoute);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsXpuDeviceRoute(string? deviceRoute) =>
        !string.IsNullOrWhiteSpace(deviceRoute) &&
        deviceRoute.StartsWith("xpu:", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(deviceRoute.AsSpan(4), out var deviceIndex) &&
        deviceIndex >= 0;

    private static bool IsCudaDeviceRoute(string? deviceRoute) =>
        !string.IsNullOrWhiteSpace(deviceRoute) &&
        deviceRoute.StartsWith("cuda:", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(deviceRoute.AsSpan(5), out var deviceIndex) &&
        deviceIndex >= 0;

    private static LoadedModelStatus? FindLoadedModel(
        ModelLoadStatus status,
        ConfigurationBackend backend,
        string backendVariantId,
        string modelPath) =>
        status.LoadedModels.FirstOrDefault(item =>
            item.IsModelLoaded &&
            item.Backend == backend &&
            string.Equals(item.BackendVariantId, backendVariantId, StringComparison.OrdinalIgnoreCase) &&
            PathsMatch(item.ModelPath, modelPath));

    private async Task TryUnloadModelAsync(
        HttpClient httpClient,
        ConfigurationBackend backend,
        string backendVariantId,
        string modelPath)
    {
        try
        {
            var status = await GetJsonAsync<ModelLoadStatus>(httpClient, "v1/application/models");
            if (FindLoadedModel(status, backend, backendVariantId, modelPath) is null)
                return;

            using var response = await httpClient.PostAsJsonAsync(
                "v1/application/models/unload",
                new ApplicationModelUnloadRequest(modelPath, backend, backendVariantId),
                JsonOptions);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                TestContext.WriteLine($"Could not unload temporary {backendVariantId} model: {responseBody}");
        }
        catch (Exception exception)
        {
            TestContext.WriteLine($"Could not unload temporary {backendVariantId} model: {exception.Message}");
        }
    }

    private static async Task AssertWebApiChatAsync(HttpClient httpClient, string modelId, string backendVariantId)
    {
        var prompt = $"Reply briefly with a greeting. Backend test: {backendVariantId}.";
        var request = new OpenAiChatRequest(modelId, [new OpenAiChatMessage("user", prompt)])
        {
            MaxTokens = 32,
            Temperature = 0
        };

        using var response = await httpClient.PostAsJsonAsync("v1/chat/completions", request, JsonOptions);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"{backendVariantId} WebAPI chat failed: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var answer = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(answer), $"{backendVariantId} WebAPI chat returned no assistant text.");
    }

    private static string GetExpectedChatBackend(ConfigurationBackend backend, string backendVariantId) => backend switch
    {
        ConfigurationBackend.Llama => backendVariantId switch
        {
            "llama.vulkan" => "Vulkan",
            "llama.cuda12" => "CUDA",
            "llama.sycl" => "SYCL",
            _ => throw new ArgumentOutOfRangeException(nameof(backendVariantId), backendVariantId, "Unsupported LLama backend variant.")
        },
        ConfigurationBackend.OpenVino => "OpenVINO",
        ConfigurationBackend.Vllm => "vLLM",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unsupported chat backend.")
    };

    private async Task AssertBrowserChatAsync(
        Uri baseAddress,
        ConfigurationBackend backend,
        string backendVariantId,
        string modelPath,
        string expectedBackend)
    {
        var playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        IPage? page = null;
        var chatCreated = false;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            page = await browser.NewPageAsync();
            page.PageError += (_, message) => TestContext.WriteLine($"Browser page error: {message}");
            page.RequestFailed += (_, request) => TestContext.WriteLine($"Browser request failed: {request.Method} {request.Url}: {request.Failure}");
            page.Console += (_, message) =>
            {
                if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
                    TestContext.WriteLine($"Browser console error: {message.Text}");
            };
            await page.GotoAsync(new Uri(baseAddress, "chats").ToString());
            await page.Locator("button[title='Neuen Chat erstellen']").ClickAsync();
            chatCreated = true;
            try
            {
                await page.Locator("#chat-model").WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 30_000
                });
            }
            catch (PlaywrightException exception)
            {
                var pageState = await page.EvaluateAsync<string>("() => JSON.stringify({ url: location.href, title: document.title, hasChatPanel: !!document.querySelector('.chat-panel'), bodyText: document.body.innerText.slice(0, 2000) })");
                TestContext.WriteLine($"Browser chat state after new-chat click: {pageState}");
                throw new InvalidOperationException("The new-chat action did not render the model selector.", exception);
            }

            await page.Locator("#chat-model").SelectOptionAsync($"{backend}|{backendVariantId}|{modelPath}");
            await page.WaitForFunctionAsync(
                "variantId => document.querySelector('.chat-panel')?.getAttribute('data-selected-backend-variant-id') === variantId",
                backendVariantId);
            await page.Locator("#chat-input").FillAsync($"Reply briefly with a greeting. Browser test: {backendVariantId}.");
            var composerState = await page.EvaluateAsync<string>("() => JSON.stringify({ model: document.querySelector('#chat-model')?.value, message: document.querySelector('#chat-input')?.value, sendDisabled: document.querySelector('.chat-composer button.send-button')?.disabled, pending: document.querySelectorAll('.chat-message.pending').length, status: document.querySelector('.composer-footer span')?.textContent })");
            TestContext.WriteLine($"Browser composer state before send: {composerState}");
            await page.Locator(".chat-composer button.send-button").ClickAsync();

            var assistantMessage = page.Locator("article.chat-message.assistant:not(.pending) p").Last;
            try
            {
                await assistantMessage.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 300_000
                });
            }
            catch (TimeoutException)
            {
                var streamState = await page.EvaluateAsync<string>("() => JSON.stringify({ isSending: document.querySelector('.chat-panel')?.getAttribute('aria-busy'), pendingText: document.querySelector('article.chat-message.pending p')?.innerText, status: document.querySelector('.composer-footer span')?.textContent })");
                TestContext.WriteLine($"Browser chat state after response timeout: {streamState}");
                throw;
            }
            var answer = await assistantMessage.InnerTextAsync();
            Assert.IsFalse(string.IsNullOrWhiteSpace(answer), $"{backendVariantId} browser chat returned no assistant text.");

            var messageMetadata = page.Locator("article.chat-message.assistant:not(.pending) .message-meta").Last;
            Assert.Contains(expectedBackend, await messageMetadata.InnerTextAsync());
        }
        finally
        {
            if (chatCreated && page is not null)
            {
                try
                {
                    var deleteButton = page.Locator(".chat-list-item.active button.chat-delete-button");
                    if (await deleteButton.CountAsync() > 0)
                        await deleteButton.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                }
                catch (PlaywrightException exception)
                {
                    TestContext.WriteLine($"Could not remove the temporary browser chat: {exception.Message}");
                }
            }

            if (browser is not null)
                await browser.CloseAsync();
            playwright.Dispose();
        }
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient httpClient, string path)
        where T : class
    {
        using var response = await httpClient.GetAsync(path);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"GET {path} failed: {responseBody}");
        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException($"GET {path} returned an empty response.");
    }

    private static bool PathsMatch(string left, string right) =>
        string.Equals(left.TrimEnd('/', '\\'), right.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
}