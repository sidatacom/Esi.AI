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

        await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("Qwen2.5", ["openvino"]));

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

        await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("Qwen", ["transformers"], Other: ["vllm"]));

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
            Libraries: ["gguf", "openvino"],
            Tasks: ["text-generation", "image-to-text"],
            ParameterRanges: ["n<1B"],
            Languages: ["de"],
            Licenses: ["license:mit"],
            Hardware: ["cuda", "vulkan"],
            Other: ["vllm", "sglang"],
            InferenceProviders: ["groq", "together"],
            BaseOnly: true,
            InferenceAvailable: false,
            Sort: "likes"));

        var queries = string.Join('&', handler.RequestUris.Select(uri => uri.Query));
        StringAssert.Contains(queries, "filter=gguf");
        StringAssert.Contains(queries, "filter=openvino");
        StringAssert.Contains(queries, "pipeline_tag=text-generation");
        StringAssert.Contains(queries, "pipeline_tag=image-to-text");
        StringAssert.Contains(queries, "num_parameters=n%3C1B");
        StringAssert.Contains(queries, "language=de");
        StringAssert.Contains(queries, "license=license%3Amit");
        StringAssert.Contains(queries, "hardware=cuda");
        StringAssert.Contains(queries, "hardware=vulkan");
        StringAssert.Contains(queries, "other=base");
        StringAssert.Contains(queries, "other=vllm");
        StringAssert.Contains(queries, "other=sglang");
        StringAssert.Contains(queries, "inference_provider=groq");
        StringAssert.Contains(queries, "inference_provider=together");
        StringAssert.Contains(queries, "sort=likes");
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

        await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("Qwen", Libraries: [], Sort: "trending"));

        var query = handler.RequestUri!.Query;
        Assert.IsFalse(query.Contains("filter=", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("pipeline_tag=", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("hardware=", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("sort=", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SearchHuggingFaceAsync_VramBudgetAndContextMatch_ReturnsOnlyFittingModels()
    {
        var library = new ModelLibraryService(
            new HttpClient(new MemoryFilterHttpMessageHandler()) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        var results = await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("fit", VramBudgetGiB: 12, ContextLength: 131_072));

        CollectionAssert.AreEquivalent(new[] { "owner/fits" }, results.Select(model => model.Id).ToArray());
        await library.DisposeAsync();
    }

    [TestMethod]
    public async Task SearchHuggingFaceAsync_ContextExceedsModelMaximum_ExcludesModel()
    {
        var library = new ModelLibraryService(
            new HttpClient(new MemoryFilterHttpMessageHandler()) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        var results = await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("context", VramBudgetGiB: 32, ContextLength: 131_072));

        CollectionAssert.AreEquivalent(new[] { "owner/fits" }, results.Select(model => model.Id).ToArray());
        await library.DisposeAsync();
    }

    [TestMethod]
    public async Task SearchHuggingFaceAsync_MissingMemoryMetadata_ExcludesModelWhenFilterActive()
    {
        var library = new ModelLibraryService(
            new HttpClient(new MemoryFilterHttpMessageHandler()) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        var results = await library.SearchHuggingFaceAsync(new HuggingFaceSearchRequest("missing", VramBudgetGiB: 32, ContextLength: 131_072));

        CollectionAssert.AreEquivalent(new[] { "owner/fits" }, results.Select(model => model.Id).ToArray());
        await library.DisposeAsync();
    }

    [TestMethod]
    public async Task GetHuggingFaceModelMetadataAsync_UnauthorizedExplainsTokenConfiguration()
    {
        var library = new ModelLibraryService(
            new HttpClient(new UnauthorizedHttpMessageHandler()) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            library.GetHuggingFaceModelMetadataAsync("owner/repository"));

        StringAssert.Contains(exception.Message, "HF_TOKEN");
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
    public async Task GetDownloadOptionsAsync_ReturnsWholeRepositoryForNonGgufLibrary()
    {
        var handler = new ModelFilesHttpMessageHandler();
        var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            null!,
            null!,
            Options.Create(new ModelLibraryOptions()));

        var options = await library.GetDownloadOptionsAsync("owner/repository", "transformers");

        Assert.AreEqual(1, options.Count);
        Assert.AreEqual(3, options[0].FileCount);
        StringAssert.StartsWith(options[0].Label, "Repository");
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
            Options.Create(new ModelLibraryOptions { Directories = [directory], MaxParallelDownloads = 2, MaxParallelFileDownloads = 4 }));

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
    public async Task RestoreDownloadsAsync_PausesIncompleteDownloadForManualResume()
    {
        var directory = Directory.CreateTempSubdirectory("esi-ai-download-restore-").FullName;
        var databasePath = Path.Combine(directory, "test.db");
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(dbOptions);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.ModelDownloads.Add(new ModelDownloadEntity
            {
                Id = Guid.NewGuid(),
                ModelId = "owner/repository",
                Library = "transformers",
                DestinationPath = directory,
                Revision = "revision",
                FileNamesJson = System.Text.Json.JsonSerializer.Serialize(new[] { "model.safetensors" }),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var handler = new TrackingDownloadHttpMessageHandler();
        await using var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            new NoOpHubContext(),
            dbContextFactory,
            Options.Create(new ModelLibraryOptions { Directories = [directory] }));

        try
        {
            await library.RestoreDownloadsAsync();

            var status = library.GetDownloads().Single();
            Assert.IsTrue(status.Paused);
            Assert.IsFalse(status.Queued);
            Assert.AreEqual(0, handler.RequestCount);

            await using var db = await dbContextFactory.CreateDbContextAsync();
            Assert.IsTrue((await db.ModelDownloads.SingleAsync()).Paused);
        }
        finally
        {
            dbContextFactory.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CancelDownloadAsync_RestoredDownload_RemovesFilesAndPersistedDownload()
    {
        var directory = Directory.CreateTempSubdirectory("esi-ai-download-restored-cancel-").FullName;
        var databasePath = Path.Combine(directory, "test.db");
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(dbOptions);
        var downloadId = Guid.NewGuid();
        var filePath = Path.Combine(directory, "model.safetensors");
        await File.WriteAllTextAsync(filePath, "partial");
        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.ModelDownloads.Add(new ModelDownloadEntity
            {
                Id = downloadId,
                ModelId = "owner/repository",
                Library = "transformers",
                DestinationPath = directory,
                Revision = "revision",
                FileNamesJson = System.Text.Json.JsonSerializer.Serialize(new[] { "model.safetensors" }),
                FileStatusesJson = System.Text.Json.JsonSerializer.Serialize(new[] { new DownloadFileStatus("model.safetensors", 7, 100, false) }),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var handler = new TrackingDownloadHttpMessageHandler();
        await using var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            new NoOpHubContext(),
            dbContextFactory,
            Options.Create(new ModelLibraryOptions { Directories = [directory] }));

        try
        {
            await library.RestoreDownloadsAsync();
            await library.CancelDownloadAsync(downloadId);

            Assert.IsNull(library.GetDownload(downloadId));
            Assert.IsFalse(File.Exists(filePath));
            await using var db = await dbContextFactory.CreateDbContextAsync();
            Assert.IsNull(await db.ModelDownloads.FindAsync(downloadId));
        }
        finally
        {
            dbContextFactory.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task StartDownloadAsync_MultipleFilesRespectFileConcurrencyLimit()
    {
        var directory = Directory.CreateTempSubdirectory("esi-ai-download-file-limit-").FullName;
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
            Options.Create(new ModelLibraryOptions { Directories = [directory], MaxParallelFileDownloads = 2 }));

        try
        {
            var downloadId = await library.StartDownloadAsync("owner/first", "first-00001-of-00002.gguf");

            while (library.GetDownload(downloadId)?.Completed != true)
                await Task.Delay(10);

            Assert.AreEqual(2, handler.MaxConcurrentDownloads);
        }
        finally
        {
            dbContextFactory.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CancelDownloadAsync_RemovesPartialFilesAndPersistedDownload()
    {
        var directory = Directory.CreateTempSubdirectory("esi-ai-download-cancel-").FullName;
        var databasePath = Path.Combine(directory, "test.db");
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(dbOptions);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();
        var handler = new CancellableDownloadHttpMessageHandler();
        await using var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            new NoOpHubContext(),
            dbContextFactory,
            Options.Create(new ModelLibraryOptions { Directories = [directory] }));

        try
        {
            var downloadId = await library.StartDownloadAsync("owner/repository", "model.gguf");
            await handler.DownloadStarted;

            await library.CancelDownloadAsync(downloadId);

            Assert.IsNull(library.GetDownload(downloadId));
            await using var db = await dbContextFactory.CreateDbContextAsync();
            Assert.IsNull(await db.ModelDownloads.FindAsync(downloadId));
            Assert.IsFalse(File.Exists(Path.Combine(directory, "model.gguf")));
        }
        finally
        {
            dbContextFactory.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CancelDownloadAsync_RemovesQueuedDownloadWithoutWaitingForSlot()
    {
        var directory = Directory.CreateTempSubdirectory("esi-ai-download-queued-cancel-").FullName;
        var databasePath = Path.Combine(directory, "test.db");
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(dbOptions);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();
        var handler = new CancellableDownloadHttpMessageHandler();
        await using var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            new NoOpHubContext(),
            dbContextFactory,
            Options.Create(new ModelLibraryOptions { Directories = [directory], MaxParallelDownloads = 1 }));

        try
        {
            var activeDownloadId = await library.StartDownloadAsync("owner/active", null, "transformers");
            await handler.DownloadStarted;
            var queuedDownloadId = await library.StartDownloadAsync("owner/queued", null, "transformers");

            await library.CancelDownloadAsync(queuedDownloadId);

            Assert.IsNull(library.GetDownload(queuedDownloadId));
            await library.CancelDownloadAsync(activeDownloadId);
        }
        finally
        {
            dbContextFactory.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RestoreDownloadsAsync_DoesNotResumeFailedDownload()
    {
        var directory = Directory.CreateTempSubdirectory("esi-ai-download-failed-restore-").FullName;
        var databasePath = Path.Combine(directory, "test.db");
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var dbContextFactory = new TestDbContextFactory(dbOptions);
        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.ModelDownloads.Add(new ModelDownloadEntity
            {
                Id = Guid.NewGuid(),
                ModelId = "owner/repository",
                Library = "transformers",
                DestinationPath = directory,
                Revision = "revision",
                FileNamesJson = System.Text.Json.JsonSerializer.Serialize(new[] { "model.safetensors" }),
                Error = "Hugging Face access was denied.",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var handler = new TrackingDownloadHttpMessageHandler();
        await using var library = new ModelLibraryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://huggingface.co/") },
            new NoOpHubContext(),
            dbContextFactory,
            Options.Create(new ModelLibraryOptions { Directories = [directory] }));

        try
        {
            await library.RestoreDownloadsAsync();
            await Task.Delay(100);

            var status = library.GetDownloads().Single();
            Assert.AreEqual("Hugging Face access was denied.", status.Error);
            Assert.IsFalse(status.Queued);
            Assert.AreEqual(0, handler.RequestCount);
        }
        finally
        {
            dbContextFactory.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task BackendModel_ReadAsync_FiltersLocalModelsByBackend()
    {
        await using var context = await TestContext.CreateAsync();
        var llamaModels = await context.DataService.BackendModel_ReadAsync(ConfigurationBackend.Llama);
        var dotLlmModels = await context.DataService.BackendModel_ReadAsync(ConfigurationBackend.DotLlm);
        var vllmModels = await context.DataService.BackendModel_ReadAsync(ConfigurationBackend.Vllm);
        var sglangModels = await context.DataService.BackendModel_ReadAsync(ConfigurationBackend.Sglang);

        Assert.AreEqual(1, llamaModels.Count);
        Assert.AreEqual(ConfigurationBackend.Llama, llamaModels[0].Backend);
        Assert.AreEqual(1, dotLlmModels.Count);
        Assert.AreEqual(ConfigurationBackend.DotLlm, dotLlmModels[0].Backend);
        Assert.AreEqual(0, vllmModels.Count);
        Assert.AreEqual(0, sglangModels.Count);
    }

    [TestMethod]
    public void ModelBackendCompatibility_FromHuggingFaceMetadata_ReturnsPythonBackends()
    {
        var backends = ModelBackendCompatibility.FromHuggingFace("transformers", ["vllm"]);

        CollectionAssert.AreEquivalent(
            new[] { ConfigurationBackend.Vllm, ConfigurationBackend.Sglang },
            backends.Where(backend => backend is ConfigurationBackend.Vllm or ConfigurationBackend.Sglang).ToArray());
    }

    [TestMethod]
    public void ModelBackendCompatibility_FromGgufHuggingFaceMetadata_DoesNotReturnPythonBackends()
    {
        var backends = ModelBackendCompatibility.FromHuggingFace("transformers", ["transformers", "gguf"]);

        CollectionAssert.AreEquivalent(
            new[] { ConfigurationBackend.Llama, ConfigurationBackend.DotLlm },
            backends.ToArray());
    }

    [TestMethod]
    public void ModelBackendCompatibility_DoesNotInferBackendsFromPipelineTag()
    {
        var backends = ModelBackendCompatibility.FromHuggingFace(null, []);

        Assert.AreEqual(0, backends.Count);
    }

    [TestMethod]
    public void ModelBackendCompatibility_CapabilitiesFromHuggingFaceMetadata_MapsToolingVisionAndThinking()
    {
        var capabilities = ModelBackendCompatibility.CapabilitiesFromHuggingFace(
            "image-text-to-text",
            ["tool-use", "reasoning"]);

        Assert.IsTrue(capabilities.ToolCalling);
        Assert.IsTrue(capabilities.ImageInput);
        Assert.IsTrue(capabilities.Thinking);
    }

    [TestMethod]
    public async Task LocalModel_UpdateAsync_ManualAssignmentControlsBackendPicker()
    {
        await using var context = await TestContext.CreateAsync();

        await context.DataService.LocalModel_UpdateAsync(new ModelCompatibilityUpdate(
            context.ModelPath,
            [ConfigurationBackend.Vllm]));

        var refreshed = (await context.DataService.LocalModel_ReadAsync()).Single(model => model.Path == context.ModelPath);
        var vllmModels = await context.DataService.BackendModel_ReadAsync(ConfigurationBackend.Vllm);
        var llamaModels = await context.DataService.BackendModel_ReadAsync(ConfigurationBackend.Llama);

        CollectionAssert.AreEquivalent(new[] { ConfigurationBackend.Vllm }, refreshed.CompatibleBackends!.ToArray());
        Assert.AreEqual(1, vllmModels.Count);
        Assert.AreEqual(0, llamaModels.Count);
    }

    [TestMethod]
    public async Task LocalModel_UpdateAsync_CapabilityUpdatePreservesBackendAssignment()
    {
        await using var context = await TestContext.CreateAsync();

        await context.DataService.LocalModel_UpdateAsync(new ModelCompatibilityUpdate(
            context.ModelPath,
            [ConfigurationBackend.Vllm]));

        var models = await context.DataService.LocalModel_UpdateAsync(new ModelCompatibilityUpdate(
            context.ModelPath,
            Capabilities: new ModelCapabilities(ToolCalling: true)));
        var model = models.Single(item => item.Path == context.ModelPath);

        CollectionAssert.AreEquivalent(
            new[] { ConfigurationBackend.Vllm },
            model.CompatibleBackends!.ToArray());
        Assert.IsTrue(model.Capabilities!.ToolCalling);
    }

    [TestMethod]
    public async Task LocalModel_UpdateAsync_FromHuggingFaceUsesRepositoryMetadata()
    {
        await using var context = await TestContext.CreateAsync(new HuggingFaceMetadataHttpMessageHandler());

        var models = await context.DataService.LocalModel_UpdateAsync(context.ModelPath, "owner/repository");
        var model = models.Single(item => item.Path == context.ModelPath);

        CollectionAssert.AreEquivalent(
            new[] { ConfigurationBackend.Vllm, ConfigurationBackend.Sglang },
            model.CompatibleBackends!.ToArray());
        Assert.AreEqual("owner/repository", model.HuggingFaceModelId);
        Assert.IsTrue(model.Capabilities!.ToolCalling);
        Assert.IsTrue(model.Capabilities.Thinking);
    }

    [TestMethod]
    public async Task LocalModel_ReadAsync_RestoresHuggingFaceIdFromCompletedDownload()
    {
        await using var context = await TestContext.CreateAsync();
        await context.AddCompletedDownloadAsync("owner/repository");

        var model = (await context.DataService.LocalModel_ReadAsync()).Single(item => item.Path == context.ModelPath);

        Assert.AreEqual("owner/repository", model.HuggingFaceModelId);
    }

    [TestMethod]
    public async Task LocalModel_ReadAsync_RestoresHuggingFaceIdFromCompletedTransformersDownload()
    {
        await using var context = await TestContext.CreateAsync();
        var modelPath = await context.AddCompletedTransformersDownloadAsync("owner/transformer");

        var model = (await context.DataService.LocalModel_ReadAsync()).Single(item => item.Path == modelPath);

        Assert.AreEqual("owner/transformer", model.HuggingFaceModelId);
    }

    [TestMethod]
    public async Task Chat_DeleteAsync_ExistingChat_RemovesChatAndMessages()
    {
        await using var context = await TestContext.CreateAsync();
        var chat = await context.DataService.Chat_CreateAsync(new CreateChatRequest("Test chat"));
        await context.AddChatMessageAsync(chat.Id);

        await context.DataService.Chat_DeleteAsync(chat.Id);

        Assert.IsNull(await context.DataService.Chat_ReadAsync(chat.Id));
        Assert.IsFalse((await context.DataService.Chat_ReadAsync()).Any(summary => summary.Id == chat.Id));
        Assert.AreEqual(0, await context.GetChatMessageCountAsync(chat.Id));
    }

    [TestMethod]
    public async Task LocalModel_DeleteAsync_WithoutFiles_HidesModelAndKeepsFile()
    {
        await using var context = await TestContext.CreateAsync();

        var models = await context.DataService.LocalModel_DeleteAsync(new ModelDeletionRequest(context.ModelPath, false));

        Assert.IsTrue(File.Exists(context.ModelPath));
        Assert.IsFalse(models.Any(model => model.Path == context.ModelPath));
    }

    [TestMethod]
    public async Task LocalModel_DeleteAsync_WithFiles_DeletesModelAndFile()
    {
        await using var context = await TestContext.CreateAsync();

        var models = await context.DataService.LocalModel_DeleteAsync(new ModelDeletionRequest(context.ModelPath, true));

        Assert.IsFalse(File.Exists(context.ModelPath));
        Assert.IsFalse(models.Any(model => model.Path == context.ModelPath));
    }

    [TestMethod]
    public async Task ModelConfiguration_CreateAsync_AllowsSameNameAcrossBackends()
    {
        await using var context = await TestContext.CreateAsync();
        var now = DateTime.UtcNow;
        var llamaConfiguration = new ModelConfiguration(
            Guid.Empty, "Shared defaults", null, context.ModelPath, false, 1, "{}", now, now, ConfigurationBackend.Llama);
        var pythonConfiguration = llamaConfiguration with { Backend = ConfigurationBackend.Vllm };

        var savedLlama = await context.DataService.ModelConfiguration_CreateAsync(llamaConfiguration);
        var savedPython = await context.DataService.ModelConfiguration_CreateAsync(pythonConfiguration);
        var configurations = await context.DataService.ModelConfiguration_ReadAsync();

        Assert.AreEqual(ConfigurationBackend.Llama, savedLlama.Backend);
        Assert.AreEqual(ConfigurationBackend.Vllm, savedPython.Backend);
        Assert.AreEqual(2, configurations.Count(configuration => configuration.Name == "Shared defaults"));
    }

    [TestMethod]
    public async Task ModelConfiguration_CreateAsync_EmptyModelPath_ThrowsArgumentException()
    {
        await using var context = await TestContext.CreateAsync();
        var configuration = new ModelConfiguration(
            Guid.Empty, "Missing model", null, string.Empty, false, 1, "{}", default, default, ConfigurationBackend.Llama);

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => context.DataService.ModelConfiguration_CreateAsync(configuration));
    }

    [TestMethod]
    public async Task LoadPythonModelAsync_SglangPreparationFails_ReturnsFailureStatus()
    {
        await using var context = await TestContext.CreateAsync();
        var request = new PythonInferenceLoadRequest(
            context.ModelPath,
            ConfigurationBackend.Sglang,
            PythonExecutable: "/missing/sglang-python");

        var status = await context.DataService.LoadPythonModelAsync(request);

        Assert.IsFalse(status.IsModelLoaded);
        Assert.IsFalse(string.IsNullOrWhiteSpace(status.LoadLog));
        Assert.IsFalse(status.LoadLog.Contains("SGLang could not load", StringComparison.OrdinalIgnoreCase));
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

        public async Task AddCompletedDownloadAsync(string modelId)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            db.ModelDownloads.Add(new ModelDownloadEntity
            {
                Id = Guid.NewGuid(),
                ModelId = modelId,
                Library = "gguf",
                DestinationPath = directory,
                FileNamesJson = System.Text.Json.JsonSerializer.Serialize(new[] { Path.GetFileName(ModelPath) }),
                Completed = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        public async Task<string> AddCompletedTransformersDownloadAsync(string modelId)
        {
            var modelDirectory = Directory.CreateDirectory(Path.Combine(directory, "transformer-model")).FullName;
            await File.WriteAllTextAsync(Path.Combine(modelDirectory, "config.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(modelDirectory, "model.safetensors"), "model");

            await using var db = await dbContextFactory.CreateDbContextAsync();
            db.ModelDownloads.Add(new ModelDownloadEntity
            {
                Id = Guid.NewGuid(),
                ModelId = modelId,
                Library = "transformers",
                DestinationPath = modelDirectory,
                FileNamesJson = "[]",
                Completed = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return modelDirectory;
        }

        public async Task AddChatMessageAsync(Guid chatId)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            db.ChatMessages.Add(new ChatMessageEntity
            {
                ConversationId = chatId,
                Role = "user",
                Content = "Hello",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        public async Task<int> GetChatMessageCountAsync(Guid chatId)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            return await db.ChatMessages.CountAsync(message => message.ConversationId == chatId);
        }

        public static async Task<TestContext> CreateAsync(HttpMessageHandler? handler = null)
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
                new HttpClient(handler ?? new HttpClientHandler()) { BaseAddress = new Uri("https://huggingface.co/") },
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
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class UnauthorizedHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
    }

    private sealed class TrackingDownloadHttpMessageHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
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

    private sealed class MemoryFilterHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (path.Equals("/api/models", StringComparison.Ordinal))
            {
                var query = request.RequestUri?.Query ?? string.Empty;
                var models = query.Contains("search=context", StringComparison.Ordinal)
                    ? "[{\"id\":\"owner/fits\",\"author\":\"owner\",\"downloads\":1,\"likes\":1},{\"id\":\"owner/short\",\"author\":\"owner\",\"downloads\":1,\"likes\":1}]"
                    : query.Contains("search=missing", StringComparison.Ordinal)
                        ? "[{\"id\":\"owner/fits\",\"author\":\"owner\",\"downloads\":1,\"likes\":1},{\"id\":\"owner/missing\",\"author\":\"owner\",\"downloads\":1,\"likes\":1}]"
                        : "[{\"id\":\"owner/fits\",\"author\":\"owner\",\"downloads\":1,\"likes\":1},{\"id\":\"owner/large\",\"author\":\"owner\",\"downloads\":1,\"likes\":1}]";
                return Task.FromResult(JsonResponse(models));
            }

            if (segments.Length >= 4 && segments[0].Equals("api", StringComparison.Ordinal) && segments[1].Equals("models", StringComparison.Ordinal))
            {
                var modelId = $"{segments[2]}/{segments[3]}";
                if (segments.Length == 6 && segments[4].Equals("tree", StringComparison.Ordinal))
                {
                    var weightSize = modelId.Equals("owner/large", StringComparison.Ordinal) ? 11_000_000_000L : 1_000_000_000L;
                    return Task.FromResult(JsonResponse($"[{{\"path\":\"model.safetensors\",\"size\":{weightSize}}}]"));
                }
                if (modelId.Equals("owner/large", StringComparison.Ordinal))
                    return Task.FromResult(JsonResponse("{\"sha\":\"revision\",\"siblings\":[{\"rfilename\":\"model.safetensors\"}]}"));
                if (modelId.Equals("owner/short", StringComparison.Ordinal))
                    return Task.FromResult(JsonResponse("{\"sha\":\"revision\",\"siblings\":[{\"rfilename\":\"model.safetensors\"}]}"));
                if (modelId.Equals("owner/missing", StringComparison.Ordinal))
                    return Task.FromResult(JsonResponse("{\"sha\":\"revision\",\"siblings\":[{\"rfilename\":\"model.safetensors\"}]}"));
                return Task.FromResult(JsonResponse("{\"sha\":\"revision\",\"siblings\":[{\"rfilename\":\"model.safetensors\"}]}"));
            }

            if (path.EndsWith("/resolve/revision/config.json", StringComparison.Ordinal))
            {
                if (path.Contains("/short/", StringComparison.Ordinal))
                    return Task.FromResult(JsonResponse("{\"num_hidden_layers\":16,\"hidden_size\":4096,\"num_attention_heads\":32,\"num_key_value_heads\":8,\"max_position_embeddings\":4096}"));
                if (path.Contains("/missing/", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
                return Task.FromResult(JsonResponse("{\"num_hidden_layers\":8,\"hidden_size\":2048,\"num_attention_heads\":32,\"num_key_value_heads\":4,\"max_position_embeddings\":131072}"));
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string content) => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class HuggingFaceMetadataHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsolutePath.Contains("/tree/", StringComparison.Ordinal) == true
                ? "[{\"path\":\"config.json\",\"size\":10}]"
                : "{\"sha\":\"revision\",\"library_name\":\"transformers\",\"pipeline_tag\":\"text-generation\",\"tags\":[\"vllm\",\"tool-use\",\"thinking\"],\"siblings\":[{\"rfilename\":\"config.json\"}]}";
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

    private sealed class CancellableDownloadHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DownloadStarted => downloadStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("/api/models/", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"sha\":\"revision\",\"siblings\":[{\"rfilename\":\"model.gguf\"}]}")
                };
            }

            downloadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
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
