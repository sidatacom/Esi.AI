using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Hubs;
using Esi.AI.Studio.Client.Services;
using Esi.AI.Studio.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class BackendCatalogIntegrationTests
{
    [TestMethod]
    public void BackendReferenceModels_DefineOneEntryPerBackend()
    {
        var references = BackendReferenceModels.All;

        Assert.AreEqual(5, references.Count);
        CollectionAssert.AreEquivalent(
            Enum.GetValues<ConfigurationBackend>(),
            references.Select(reference => reference.Backend).ToArray());
        Assert.IsTrue(references.All(reference => !string.IsNullOrWhiteSpace(reference.ModelId)));
        Assert.IsTrue(references.All(reference => !string.IsNullOrWhiteSpace(reference.EnvironmentVariable)));
    }

    [TestMethod]
    public async Task SearchHuggingFaceAsync_UsesOpenVinoLibraryFilter()
    {
        var handler = new RecordingHttpMessageHandler();
        var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("Qwen2.5", "openvino"));

        StringAssert.Contains(handler.RequestUri!.Query, "filter=openvino");
    }

    [TestMethod]
    public async Task SearchHuggingFaceAsync_UsesAppFilter()
    {
        var handler = new RecordingHttpMessageHandler();
        var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("Qwen", "transformers", Other: "vllm"));

        StringAssert.Contains(handler.RequestUri!.Query, "filter=transformers");
        StringAssert.Contains(handler.RequestUri.Query, "other=vllm");
    }

    [TestMethod]
    public async Task SearchHuggingFaceAsync_AllFilters_UsesHuggingFaceQueryParameters()
    {
        var handler = new RecordingHttpMessageHandler();
        var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest(
            "Qwen",
            Library: "gguf",
            Task: "text-generation",
            ParameterRange: "n<1B",
            Language: "de",
            License: "license:mit",
            Hardware: "cuda",
            Other: "vllm",
            InferenceProvider: "groq",
            BaseOnly: true,
            InferenceAvailable: false,
            Sort: "likes"));

        var query = handler.RequestUri!.Query;
        StringAssert.Contains(query, "filter=gguf");
        StringAssert.Contains(query, "pipeline_tag=text-generation");
        StringAssert.Contains(query, "num_parameters=n%3C1B");
        StringAssert.Contains(query, "language=de");
        StringAssert.Contains(query, "license=license%3Amit");
        StringAssert.Contains(query, "hardware=cuda");
        StringAssert.Contains(query, "other=base");
        StringAssert.Contains(query, "other=vllm");
        StringAssert.Contains(query, "inference_provider=groq");
        StringAssert.Contains(query, "sort=likes");
    }

    [TestMethod]
    public async Task SearchHuggingFaceAsync_TrendingWithoutFilters_OmitsOptionalQueryParameters()
    {
        var handler = new RecordingHttpMessageHandler();
        var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("Qwen", Library: "", Sort: "trending"));

        var query = handler.RequestUri!.Query;
        Assert.IsFalse(query.Contains("filter=", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("pipeline_tag=", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("hardware=", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("sort=", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetDownloadOptionsAsync_SeparatesQuantizationsAndGroupsShards()
    {
        var handler = new ModelFilesHttpMessageHandler();
        var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        var options = await library.GetDownloadOptionsAsync("owner/repository");

        Assert.AreEqual(2, options.Count);
        var q4Option = options.Single(option => option.FileName.Contains("Q4_K_M", StringComparison.Ordinal));
        var q8Option = options.Single(option => option.FileName.Contains("Q8_0", StringComparison.Ordinal));
        Assert.AreEqual(2, q4Option.FileCount);
        Assert.AreEqual(1, q8Option.FileCount);
        Assert.AreEqual(3_000_000_000L, q4Option.SizeInBytes);
        Assert.AreEqual(1_500_000_000L, q8Option.SizeInBytes);
        StringAssert.Contains(q4Option.Label, "2 Dateien");
        StringAssert.Contains(q4Option.Label, "2.79 GiB");
    }

    [TestMethod]
    public async Task StartDownloadAsync_MultipleRequestsRunInParallel()
    {
        var directory = Directory.CreateTempSubdirectory("esi-ai-download-queue-").FullName;
        var handler = new QueuedDownloadHttpMessageHandler();
        var databasePath = Path.Combine(directory, "test.db");
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(dbOptions);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();
        await using var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            new NoOpHubContext(),
            dbContextFactory,
            Options.Create(new ModelLibraryOptions { Directories = [directory], MaxParallelDownloads = 2 }));

        try
        {
            var downloadIds = await Task.WhenAll(
                library.StartDownloadAsync("owner/first", "first-00001-of-00002.gguf"),
                library.StartDownloadAsync("owner/second", "second-00001-of-00002.gguf"));

            foreach (var downloadId in downloadIds)
            {
                while (library.GetDownload(downloadId)?.Completed != true)
                    await Task.Delay(10);
            }

            Assert.AreEqual(2, downloadIds.Select(downloadId => library.GetDownload(downloadId)?.Files?.Count).Min());
            Assert.AreEqual(4, handler.MaxConcurrentDownloads);
        }
        finally
        {
            dbContextFactory.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetBackendModelsAsync_FiltersLocalModelsByBackend()
    {
        await using var context = await TestContext.CreateAsync();
        var llamaModels = await context.DataService.GetBackendModelsAsync(ConfigurationBackend.Llama);
        var dotLlmModels = await context.DataService.GetBackendModelsAsync(ConfigurationBackend.DotLlm);
        var vllmModels = await context.DataService.GetBackendModelsAsync(ConfigurationBackend.Vllm);
        var sglangModels = await context.DataService.GetBackendModelsAsync(ConfigurationBackend.Sglang);

        Assert.AreEqual(1, llamaModels.Count);
        Assert.AreEqual(ConfigurationBackend.Llama, llamaModels[0].Backend);
        Assert.AreEqual(1, dotLlmModels.Count);
        Assert.AreEqual(ConfigurationBackend.DotLlm, dotLlmModels[0].Backend);
        Assert.AreEqual(0, vllmModels.Count);
        Assert.AreEqual(0, sglangModels.Count);
    }

    [TestMethod]
    public async Task SaveModelConfigurationProfileAsync_AllowsSameNameAcrossBackends()
    {
        await using var context = await TestContext.CreateAsync();
        var now = DateTime.UtcNow;
        var llamaProfile = new ModelConfigurationProfile(
            Guid.Empty, "Shared defaults", null, context.ModelPath, false, 1, "{}", now, now, ConfigurationBackend.Llama);
        var pythonProfile = llamaProfile with { Backend = ConfigurationBackend.Vllm };

        var savedLlama = await context.DataService.SaveModelConfigurationProfileAsync(llamaProfile);
        var savedPython = await context.DataService.SaveModelConfigurationProfileAsync(pythonProfile);
        var profiles = await context.DataService.GetModelConfigurationProfilesAsync();

        Assert.AreEqual(ConfigurationBackend.Llama, savedLlama.Backend);
        Assert.AreEqual(ConfigurationBackend.Vllm, savedPython.Backend);
        Assert.AreEqual(2, profiles.Count(profile => profile.Name == "Shared defaults"));
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly string directory;
        private readonly string databasePath;
        private readonly TestDbContextFactory dbContextFactory;

        private TestContext(string directory, string databasePath, TestDbContextFactory dbContextFactory, DataService dataService, string modelPath)
        {
            this.directory = directory;
            this.databasePath = databasePath;
            this.dbContextFactory = dbContextFactory;
            DataService = dataService;
            ModelPath = modelPath;
        }

        public DataService DataService { get; }
        public string ModelPath { get; }

        public static async Task<TestContext> CreateAsync()
        {
            var directory = Directory.CreateTempSubdirectory("esi-ai-studio-tests-").FullName;
            var modelPath = Path.Combine(directory, "local.gguf");
            await File.WriteAllBytesAsync(modelPath, [0x47, 0x47, 0x55, 0x46]);
            await File.WriteAllTextAsync(Path.Combine(directory, "local.safetensors"), "not a GGUF model");

            var databasePath = Path.Combine(directory, "test.db");
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var dbContextFactory = new TestDbContextFactory(options);
            await using (var db = await dbContextFactory.CreateDbContextAsync())
                await db.Database.EnsureCreatedAsync();

            var modelLibrary = new ModelLibraryService(
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
            return new TestContext(directory, databasePath, dbContextFactory, dataService, modelPath);
        }

        public ValueTask DisposeAsync()
        {
            dbContextFactory.Dispose();
            TryDelete(databasePath);
            TryDelete(directory);
            return ValueTask.CompletedTask;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ModelFilesHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsolutePath.Contains("/tree/", StringComparison.Ordinal) == true
                ? "[{\"path\":\"model.Q4_K_M-00001-of-00002.gguf\",\"size\":1000000000},{\"path\":\"model.Q4_K_M-00002-of-00002.gguf\",\"size\":2000000000},{\"path\":\"model.Q8_0.gguf\",\"size\":1500000000}]"
                : "{\"sha\":\"revision\",\"siblings\":[{\"rfilename\":\"model.Q4_K_M-00001-of-00002.gguf\"},{\"rfilename\":\"model.Q4_K_M-00002-of-00002.gguf\"},{\"rfilename\":\"model.Q8_0.gguf\"}]}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class QueuedDownloadHttpMessageHandler : HttpMessageHandler
    {
        private int activeDownloads;
        private int maxConcurrentDownloads;

        public int MaxConcurrentDownloads => Volatile.Read(ref maxConcurrentDownloads);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/api/models/", StringComparison.Ordinal))
            {
                var filePrefix = path.EndsWith("/first", StringComparison.Ordinal) ? "first" : "second";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"sha\":\"revision\",\"siblings\":[{{\"rfilename\":\"{filePrefix}-00001-of-00002.gguf\"}},{{\"rfilename\":\"{filePrefix}-00002-of-00002.gguf\"}}]}}")
                };
            }

            var active = Interlocked.Increment(ref activeDownloads);
            while (active > Volatile.Read(ref maxConcurrentDownloads) &&
                Interlocked.CompareExchange(ref maxConcurrentDownloads, active, Volatile.Read(ref maxConcurrentDownloads)) != active)
            {
            }
            try
            {
                await Task.Delay(50, cancellationToken);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([0x47, 0x47, 0x55, 0x46])
                };
            }
            finally
            {
                Interlocked.Decrement(ref activeDownloads);
            }
        }
    }

    private sealed class NoOpHubContext : IHubContext<DataHub>
    {
        public IHubClients Clients { get; } = new NoOpHubClients();
        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class NoOpHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NoOpClientProxy();

        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NoOpClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>, IDisposable
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));

        public void Dispose()
        {
        }
    }
}
