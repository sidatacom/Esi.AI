using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Esi.AI.Studio.Contracts;
using Esi.AI.Studio.Data;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.EntityFrameworkCore;

namespace Esi.AI.Studio.Services;

public sealed class DataService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILocalModelCatalog localModelCatalog,
    IModelDirectoryCatalog modelDirectoryCatalog,
    IHuggingFaceCatalog huggingFaceCatalog,
    IModelDownloadManager downloadManager,
    OpenVinoDiagnosticsService openVinoDiagnostics,
    OpenVinoDriverInstaller openVinoInstaller,
    ModelRuntime modelRuntime,
    BackendPrerequisiteProvisioner? backendPrerequisites = null,
    BackendRequirementMonitor? requirementMonitor = null,
    BackendRuntimeInstaller? backendRuntimeInstaller = null,
    IBackendRuntimeStatusPublisher? backendRuntimePublisher = null,
    IInferenceService? inferenceService = null) : IDataService
{
    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IInferenceService effectiveInferenceService = inferenceService ?? new InferenceService(modelRuntime, new InferenceScheduler());

    #region BackendRequirements

    public Task<BackendRequirementState> GetBackendRequirementStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(requirementMonitor?.Current ?? new BackendRequirementState([], DateTimeOffset.MinValue));

    #endregion

    #region LocalModels

    public async Task<IReadOnlyList<LocalModel>> LocalModel_ReadAsync(CancellationToken cancellationToken = default)
    {
        var scannedModels = await localModelCatalog.ScanLocalModelsAsync(cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var metadata = await db.ModelMetadata
            .ToDictionaryAsync(item => item.ModelPath, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var downloadedModelIds = await GetCompletedDownloadModelIdsAsync(db, cancellationToken);
        var result = new List<LocalModel>(scannedModels.Count);
        var now = DateTime.UtcNow;

        foreach (var model in scannedModels)
        {
            if (!metadata.TryGetValue(model.Path, out var entity))
            {
                entity = new ModelMetadataEntity
                {
                    Id = Guid.NewGuid(),
                    ModelPath = model.Path,
                    CompatibleBackendsJson = JsonSerializer.Serialize(ModelBackendCompatibility.ForFormat(model.Format)),
                    CapabilitiesJson = JsonSerializer.Serialize(new ModelCapabilities()),
                    UpdatedAtUtc = now
                };
                db.ModelMetadata.Add(entity);
                metadata[model.Path] = entity;
            }

            if (entity.IsDeleted)
                continue;

            if (string.IsNullOrWhiteSpace(entity.HuggingFaceModelId) && downloadedModelIds.TryGetValue(model.Path, out var huggingFaceModelId))
            {
                entity.HuggingFaceModelId = huggingFaceModelId;
                entity.UpdatedAtUtc = now;
            }

            var compatibleBackends = JsonSerializer.Deserialize<ConfigurationBackend[]>(entity.CompatibleBackendsJson) ?? [];
            var capabilities = JsonSerializer.Deserialize<ModelCapabilities>(entity.CapabilitiesJson) ?? new ModelCapabilities();
            result.Add(new LocalModel(model.Name, model.Path, model.SizeInBytes, model.LastWriteTimeUtc, model.Format, compatibleBackends, entity.HuggingFaceModelId, capabilities));
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(cancellationToken);

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, string>> GetCompletedDownloadModelIdsAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var downloads = await db.ModelDownloads.AsNoTracking()
            .Where(download => download.Completed && download.Error == null)
            .ToArrayAsync(cancellationToken);
        var modelIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var download in downloads)
        {
            if (download.Library.Equals("openvino", StringComparison.OrdinalIgnoreCase) ||
                download.Library.Equals("transformers", StringComparison.OrdinalIgnoreCase))
            {
                modelIds[Path.GetFullPath(download.DestinationPath)] = download.ModelId;
                continue;
            }

            var fileNames = JsonSerializer.Deserialize<string[]>(download.FileNamesJson) ?? [];
            foreach (var fileName in fileNames)
            {
                var modelPath = Path.GetFullPath(Path.Combine(download.DestinationPath, fileName.Replace('/', Path.DirectorySeparatorChar)));
                modelIds[modelPath] = download.ModelId;
            }
        }

        return modelIds;
    }

    public IReadOnlyList<string> GetModelDirectories() => modelDirectoryCatalog.GetModelDirectories();

    public Task<IReadOnlyList<string>> ModelDirectory_ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GetModelDirectories());

    public async Task<IReadOnlyList<HuggingFaceModel>> SearchModelsAsync(HuggingFaceSearchRequest request, CancellationToken cancellationToken = default) =>
        (await huggingFaceCatalog.SearchHuggingFaceAsync(request, cancellationToken))
        .Select(model => new HuggingFaceModel(
            model.Id,
            model.Author,
            model.Downloads,
            model.Likes,
            model.LastModified,
            model.LibraryName,
            model.PipelineTag,
            model.Tags,
            ModelBackendCompatibility.FromHuggingFace(model.LibraryName, model.Tags))).ToArray();

    public async Task<IReadOnlyList<LocalModel>> LocalModel_UpdateAsync(ModelCompatibilityUpdate update, CancellationToken cancellationToken = default)
    {
        var modelPath = Path.GetFullPath(update.ModelPath.Trim());
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelMetadata.SingleOrDefaultAsync(item => item.ModelPath == modelPath, cancellationToken);
        entity ??= new ModelMetadataEntity { Id = Guid.NewGuid(), ModelPath = modelPath };
        var existingBackends = JsonSerializer.Deserialize<ConfigurationBackend[]>(entity.CompatibleBackendsJson) ?? [];
        var compatibleBackends = update.CompatibleBackends is null
            ? existingBackends
            : NormalizeBackends(update.CompatibleBackends);
        entity.CompatibleBackendsJson = JsonSerializer.Serialize(compatibleBackends);
        entity.CapabilitiesJson = JsonSerializer.Serialize(update.Capabilities ?? new ModelCapabilities());
        entity.HuggingFaceModelId = string.IsNullOrWhiteSpace(update.HuggingFaceModelId) ? null : update.HuggingFaceModelId.Trim();
        entity.IsManuallyConfigured = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        if (db.Entry(entity).State == EntityState.Detached)
            db.ModelMetadata.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return await LocalModel_ReadAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LocalModel>> LocalModel_UpdateAsync(
        string modelPath,
        string huggingFaceModelId,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(modelPath.Trim());
        var normalizedModelId = huggingFaceModelId.Trim();
        var metadata = await huggingFaceCatalog.GetHuggingFaceModelMetadataAsync(normalizedModelId, cancellationToken);
        var compatibleBackends = ModelBackendCompatibility.FromHuggingFace(metadata.LibraryName, metadata.Tags);

        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var entity = await db.ModelMetadata.SingleOrDefaultAsync(item => item.ModelPath == normalizedPath, cancellationToken);
            entity ??= new ModelMetadataEntity { Id = Guid.NewGuid(), ModelPath = normalizedPath };
            entity.CompatibleBackendsJson = JsonSerializer.Serialize(compatibleBackends);
            entity.CapabilitiesJson = JsonSerializer.Serialize(ModelBackendCompatibility.CapabilitiesFromHuggingFace(metadata.PipelineTag, metadata.Tags));
            entity.HuggingFaceModelId = normalizedModelId;
            entity.HuggingFaceRevision = metadata.Revision;
            entity.HuggingFaceSynchronizedAtUtc = DateTime.UtcNow;
            entity.IsManuallyConfigured = false;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            if (db.Entry(entity).State == EntityState.Detached)
                db.ModelMetadata.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await LocalModel_ReadAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LocalModel>> LocalModel_DeleteAsync(
        ModelDeletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var modelPath = Path.GetFullPath(request.ModelPath.Trim());
        if (!IsWithinModelDirectory(modelPath))
            throw new InvalidOperationException("The model path is outside the configured model directories.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var metadata = await db.ModelMetadata.SingleOrDefaultAsync(item => item.ModelPath == modelPath, cancellationToken);
        if (request.DeleteFiles)
            DeleteModelFiles(modelPath);

        if (metadata is not null)
        {
            if (request.DeleteFiles)
                db.ModelMetadata.Remove(metadata);
            else
            {
                metadata.IsDeleted = true;
                metadata.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        else if (!request.DeleteFiles)
        {
            db.ModelMetadata.Add(new ModelMetadataEntity
            {
                Id = Guid.NewGuid(),
                ModelPath = modelPath,
                IsDeleted = true,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        var models = await db.Models.Where(model => model.Path == modelPath).ToArrayAsync(cancellationToken);
        db.Models.RemoveRange(models);
        await db.SaveChangesAsync(cancellationToken);
        return await LocalModel_ReadAsync(cancellationToken);
    }

    private bool IsWithinModelDirectory(string modelPath) => modelDirectoryCatalog.GetModelDirectories()
        .Select(Path.GetFullPath)
        .Any(directory =>
        {
            var relativePath = Path.GetRelativePath(directory, modelPath);
            return relativePath is not "." and not ".." &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relativePath);
        });

    private static void DeleteModelFiles(string modelPath)
    {
        if (File.Exists(modelPath))
            File.Delete(modelPath);
        else if (Directory.Exists(modelPath))
            Directory.Delete(modelPath, recursive: true);
    }

    #endregion

    #region ModelDownloads

    public Task<Guid> ModelDownload_CreateAsync(ModelDownloadRequest request, CancellationToken cancellationToken = default) =>
        downloadManager.StartDownloadAsync(request.ModelId, request.FileName, request.Library, cancellationToken);

    public Task<IReadOnlyList<ModelDownloadOption>> ModelDownload_ReadOptionsAsync(string modelId, string library = "gguf", CancellationToken cancellationToken = default) =>
        downloadManager.GetDownloadOptionsAsync(modelId, library, cancellationToken);

    public Task ModelDownload_UpdateAsync(Guid id, bool paused, CancellationToken cancellationToken = default) =>
        paused ? downloadManager.PauseDownloadAsync(id, cancellationToken) : downloadManager.ResumeDownloadAsync(id, cancellationToken);

    public Task ModelDownload_DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        downloadManager.CancelDownloadAsync(id, cancellationToken);

    public Task ModelDownload_DeleteCompletedAsync(CancellationToken cancellationToken = default) =>
        downloadManager.DeleteCompletedDownloadsAsync(cancellationToken);

    public DownloadStatus? ModelDownload_Read(Guid id)
    {
        var status = downloadManager.GetDownload(id);
        return status is null ? null : new DownloadStatus(status.Id, status.ModelId, status.FileName, status.DestinationPath,
            status.BytesDownloaded, status.TotalBytes, status.Completed, status.Error, status.Paused, status.Queued, status.Files);
    }

    /// <summary>Returns the current model download state for SignalR synchronization.</summary>
    public IReadOnlyList<DownloadStatus> ModelDownload_Read() =>
        downloadManager.GetDownloads().Select(status => new DownloadStatus(
            status.Id,
            status.ModelId,
            status.FileName,
            status.DestinationPath,
            status.BytesDownloaded,
            status.TotalBytes,
            status.Completed,
            status.Error,
            status.Paused,
            status.Queued,
            status.Files)).ToArray();

    public Task<DownloadStatus?> ModelDownload_ReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(ModelDownload_Read(id));

    public Task<IReadOnlyList<DownloadStatus>> ModelDownload_ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ModelDownload_Read());

    #endregion

    #region ModelSelection

    public async Task<ModelStatus> SelectModelAsync(SelectModelRequest request, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(request.Path) || !request.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A fully qualified GGUF path is required.");
        var settings = (await ModelSettings_ReadAsync(cancellationToken))
            .FirstOrDefault(item => item.Backend == ConfigurationBackend.Llama);
        var requestSettings = settings is null
            ? new LoadModelRequest(request.Path, "Vulkan", 0, (uint)LlamaContextSize.Context128K,
                new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase), null)
            : JsonSerializer.Deserialize<LoadModelRequest>(settings.ConfigurationJson)
                ?? throw new InvalidOperationException("The persisted model settings are invalid.");
        await ModelSettings_UpdateAsync(new ModelSettings(
            request.Path,
            ConfigurationBackend.Llama,
            JsonSerializer.Serialize(requestSettings with { ModelPath = request.Path }),
            settings?.ConfigurationId), cancellationToken);
        return new ModelStatus(request.Path, requestSettings.Backend, requestSettings.GpuLayerCount, requestSettings.ContextSize, 0, false);
    }

    #endregion

    #region ModelSettings

    public async Task<IReadOnlyList<ModelSettings>> ModelSettings_ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return (await db.ModelSettings.AsNoTracking().OrderBy(item => item.Backend).ToArrayAsync(cancellationToken))
            .Select(ToModelSettings).ToArray();
    }

    public async Task ModelSettings_UpdateAsync(ModelSettings settings, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!Enum.IsDefined(settings.Backend))
            throw new ArgumentException("A valid model backend is required.", nameof(settings));
        var entity = await db.ModelSettings.SingleOrDefaultAsync(item => item.Backend == settings.Backend, cancellationToken);
        entity ??= new ModelSettingsEntity { Backend = settings.Backend };
        entity.ModelPath = settings.ModelPath;
        entity.ConfigurationJson = settings.ConfigurationJson;
        entity.ConfigurationId = settings.ConfigurationId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        if (db.Entry(entity).State == EntityState.Detached)
            db.ModelSettings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Models

    public async Task<IReadOnlyList<Model>> Model_ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var models = await db.Models.AsNoTracking()
            .OrderBy(model => model.Name)
            .ToArrayAsync(cancellationToken);
        return models.Select(ToModel).ToArray();
    }

    /// <summary>Synchronizes and returns internal models together with persisted load configurations.</summary>
    public async Task<ApplicationModelCatalog> ApplicationModelCatalog_ReadAsync(CancellationToken cancellationToken = default)
    {
        var models = await Model_UpdateAsync(cancellationToken);
        var configurations = await ModelConfiguration_ReadAsync(cancellationToken);
        return new ApplicationModelCatalog(models, configurations);
    }

    /// <summary>Loads an internal model using the persisted configuration selected by its internal ID.</summary>
    public async Task<ModelLoadStatus> LoadConfiguredModelAsync(
        ApplicationModelLoadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ModelId == Guid.Empty)
            throw new ArgumentException("ModelId is required.", nameof(request));
        if (request.ConfigurationId == Guid.Empty)
            throw new ArgumentException("ConfigurationId is required.", nameof(request));

        var model = (await Model_UpdateAsync(cancellationToken))
            .SingleOrDefault(item => item.Id == request.ModelId)
            ?? throw new KeyNotFoundException($"The model '{request.ModelId}' was not found.");
        var configuration = await ModelConfiguration_ReadAsync(request.ConfigurationId, cancellationToken)
            ?? throw new KeyNotFoundException($"The model configuration '{request.ConfigurationId}' was not found.");
        if (!string.Equals(Path.GetFullPath(model.Path), Path.GetFullPath(configuration.ModelPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected model configuration does not belong to the selected model.");

        switch (configuration.Backend)
        {
            case ConfigurationBackend.Llama:
                await LoadModelAsync(DeserializeConfiguration<LoadModelRequest>(configuration.ConfigurationJson) with { ModelPath = model.Path }, cancellationToken);
                break;
            case ConfigurationBackend.OpenVino:
                await modelRuntime.LoadAsync(DeserializeConfiguration<OpenVinoLoadRequest>(configuration.ConfigurationJson) with { ModelPath = model.Path }, cancellationToken);
                break;
            case ConfigurationBackend.Vllm:
            case ConfigurationBackend.Sglang:
                await LoadPythonModelAsync(DeserializeConfiguration<PythonInferenceLoadRequest>(configuration.ConfigurationJson) with
                {
                    ModelPath = model.Path,
                    Backend = configuration.Backend
                }, cancellationToken);
                break;
            case ConfigurationBackend.DotLlm:
                await LoadDotLlmModelAsync(DeserializeConfiguration<DotLlmLoadRequest>(configuration.ConfigurationJson) with { ModelPath = model.Path }, cancellationToken);
                break;
            default:
                throw new ArgumentException("The model configuration backend is not supported.", nameof(request));
        }

        return modelRuntime.LoadedModel_Read();
    }

    public async Task<IReadOnlyList<BackendModel>> BackendModel_ReadAsync(ConfigurationBackend backend, CancellationToken cancellationToken = default)
    {
        var models = (await LocalModel_ReadAsync(cancellationToken))
            .Where(model => model.CompatibleBackends?.Contains(backend) == true)
            .ToArray();
        return models.Select(model => new BackendModel(
            model.Name,
            model.Path,
            model.SizeInBytes,
            model.LastWriteTimeUtc,
            backend,
            CompatibleBackends: model.CompatibleBackends)).ToArray();
    }

    private static IReadOnlyList<ConfigurationBackend> NormalizeBackends(IEnumerable<ConfigurationBackend> backends) =>
        Enum.GetValues<ConfigurationBackend>().Where(backends.Contains).ToArray();

    public async Task<IReadOnlyList<Model>> Model_UpdateAsync(CancellationToken cancellationToken = default)
    {
        var models = await LocalModel_ReadAsync(cancellationToken);
        var discoveredModels = models.Select(model => new Model(
            Guid.Empty, model.Name, model.Path, model.SizeInBytes, model.LastWriteTimeUtc)).ToArray();
        if (discoveredModels.Length == 0)
            return await Model_ReadAsync(cancellationToken);

        await SyncModelsCoreAsync(discoveredModels, cancellationToken);
        return await Model_ReadAsync(cancellationToken);
    }

    public async Task SetModelConfigurationAsync(string modelPath, Guid? configurationId, CancellationToken cancellationToken = default)
    {
        var normalizedPath = modelPath.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.Models.SingleOrDefaultAsync(item => item.Path == normalizedPath, cancellationToken);
        if (model is null)
            return;

        if (configurationId.HasValue)
        {
            var configuration = await db.ModelConfigurations.SingleOrDefaultAsync(item => item.Id == configurationId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("The model configuration was not found.");
            if (!string.Equals(configuration.ModelPath.Trim(), normalizedPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The model configuration does not belong to the selected model.", nameof(configurationId));
        }

        model.ConfigurationId = configurationId;
        model.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncModelsCoreAsync(IReadOnlyList<Model> models, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Models.ToDictionaryAsync(model => model.Path, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var incomingPaths = models.Select(model => model.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        foreach (var model in models)
        {
            if (existing.TryGetValue(model.Path, out var entity))
            {
                entity.Name = model.Name;
                entity.SizeInBytes = model.SizeInBytes;
                entity.LastWriteTimeUtc = model.LastWriteTimeUtc;
                entity.UpdatedAtUtc = now;
            }
            else
            {
                db.Models.Add(new ModelEntity
                {
                    Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
                    Name = model.Name,
                    Path = model.Path,
                    SizeInBytes = model.SizeInBytes,
                    LastWriteTimeUtc = model.LastWriteTimeUtc,
                    UpdatedAtUtc = now,
                    ConfigurationId = model.ConfigurationId
                });
            }
        }
        db.Models.RemoveRange(existing.Values.Where(model => !incomingPaths.Contains(model.Path)));
        await db.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region ModelConfigurations

    public async Task<IReadOnlyList<ModelConfiguration>> ModelConfiguration_ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.ModelConfigurations.AsNoTracking()
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.Name)
            .ToArrayAsync(cancellationToken);
        return entities.Select(ToConfiguration).ToArray();
    }

    public async Task<ModelConfiguration?> ModelConfiguration_ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelConfigurations.AsNoTracking()
            .Where(profile => profile.Id == id)
            .SingleOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToConfiguration(entity);
    }

    public Task<ModelConfiguration> ModelConfiguration_CreateAsync(ModelConfiguration configuration, CancellationToken cancellationToken = default) =>
        ModelConfiguration_SaveAsync(configuration with { Id = Guid.Empty }, cancellationToken);

    public Task<ModelConfiguration> ModelConfiguration_UpdateAsync(ModelConfiguration configuration, CancellationToken cancellationToken = default) =>
        ModelConfiguration_SaveAsync(configuration, cancellationToken);

    private async Task<ModelConfiguration> ModelConfiguration_SaveAsync(ModelConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(configuration);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var duplicateName = await db.ModelConfigurations.AnyAsync(item =>
            item.Name == configuration.Name && item.Backend == configuration.Backend && item.Id != configuration.Id, cancellationToken);
        if (duplicateName)
            throw new InvalidOperationException($"A model configuration named '{configuration.Name}' already exists.");

        var now = DateTime.UtcNow;
        var entity = configuration.Id == Guid.Empty
            ? new ModelConfigurationEntity { Id = Guid.NewGuid(), CreatedAtUtc = now }
            : await db.ModelConfigurations.SingleOrDefaultAsync(item => item.Id == configuration.Id, cancellationToken)
                ?? throw new KeyNotFoundException("The model configuration was not found.");
        entity.Name = configuration.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(configuration.Description) ? null : configuration.Description.Trim();
        entity.ModelPath = configuration.ModelPath.Trim();
        entity.Backend = configuration.Backend;
        entity.IsDefault = configuration.IsDefault;
        entity.SchemaVersion = configuration.SchemaVersion < 1 ? 1 : configuration.SchemaVersion;
        entity.ConfigurationJson = configuration.ConfigurationJson;
        entity.UpdatedAtUtc = now;
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();
        if (db.Entry(entity).State == EntityState.Detached)
            db.ModelConfigurations.Add(entity);

        if (entity.IsDefault)
            await ClearOtherDefaultsAsync(db, entity.Id, cancellationToken);
        else if (!await db.ModelConfigurations.AnyAsync(item => item.IsDefault && item.Id != entity.Id, cancellationToken))
            entity.IsDefault = true;

        await db.SaveChangesAsync(cancellationToken);
        return ToConfiguration(entity);
    }

    public async Task ModelConfiguration_DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelConfigurations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("The model configuration was not found.");
        db.ModelConfigurations.Remove(entity);
        if (entity.IsDefault)
        {
            var replacement = await db.ModelConfigurations
                .Where(item => item.Id != id)
                .OrderBy(item => item.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (replacement is not null)
                replacement.IsDefault = true;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ModelConfiguration_SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelConfigurations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("The model configuration was not found.");
        await ClearOtherDefaultsAsync(db, id, cancellationToken);
        entity.IsDefault = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Chats

    public Task<PersistedChat> Chat_CreateAsync(CreateChatRequest request, CancellationToken cancellationToken = default) =>
        Chat_CreateCoreAsync(request.Title, cancellationToken);

    public async Task<IReadOnlyList<ChatSummary>> Chat_ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ChatConversations.AsNoTracking().OrderByDescending(chat => chat.UpdatedAtUtc)
            .Select(chat => new ChatSummary(chat.Id, chat.Title, chat.UpdatedAtUtc, chat.Messages.Count)).ToArrayAsync(cancellationToken);
    }

    private async Task<PersistedChat> Chat_CreateCoreAsync(string? title, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var chat = new ChatConversationEntity { Id = Guid.NewGuid(), Title = string.IsNullOrWhiteSpace(title) ? "Neuer Chat" : title.Trim(), CreatedAtUtc = now, UpdatedAtUtc = now };
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.ChatConversations.Add(chat);
        await db.SaveChangesAsync(cancellationToken);
        return ToChat(chat);
    }

    public async Task<PersistedChat?> Chat_ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.ChatConversations.AsNoTracking().Include(item => item.Messages).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return chat is null ? null : ToChat(chat);
    }

    public async Task Chat_DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.ChatConversations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (chat is null)
            return;

        db.ChatConversations.Remove(chat);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<PersistedChat?> Chat_UpdateCoreAsync(Guid id, string userContent, GenerationResult generation, string modelPath, string backend, CancellationToken cancellationToken = default) =>
        await PersistChatUpdateAsync(id, userContent, generation.Text, modelPath, backend, generation.TokenCount, generation.TokensPerSecond, cancellationToken);

    private async Task<PersistedChat?> PersistChatUpdateAsync(Guid id, string userContent, string assistantContent, string modelPath, string backend, int? tokenCount, double? tokensPerSecond, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.ChatConversations.Include(item => item.Messages).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (chat is null) return null;
        var now = DateTime.UtcNow;
        chat.Messages.Add(new ChatMessageEntity { Role = "user", Content = userContent, CreatedAtUtc = now });
        chat.Messages.Add(new ChatMessageEntity { Role = "assistant", Content = assistantContent, ModelPath = modelPath, Backend = backend, TokenCount = tokenCount, TokensPerSecond = tokensPerSecond, CreatedAtUtc = now });
        chat.UpdatedAtUtc = now;
        if (chat.Title == "Neuer Chat") chat.Title = userContent.Length > 60 ? userContent[..60] : userContent;
        await db.SaveChangesAsync(cancellationToken);
        return ToChat(chat);
    }

    #endregion

    #region LoadedModel

    public Task<ModelLoadStatus> LoadedModel_ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(modelRuntime.LoadedModel_Read());

    public async Task<ModelLoadStatus> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ModelPath) ||
            !string.Equals(Path.GetExtension(request.ModelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("LLama loading requires a .gguf model path.", nameof(request));

        await modelRuntime.LoadAsync(request, cancellationToken);
        return modelRuntime.LoadedModel_Read();
    }

    public async Task<ModelLoadStatus> LoadPythonModelAsync(PythonInferenceLoadRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Backend is not (ConfigurationBackend.Vllm or ConfigurationBackend.Sglang))
            throw new ArgumentException("A vLLM or SGLang backend is required.", nameof(request));

        try
        {
            await modelRuntime.LoadAsync(request, cancellationToken);
            return modelRuntime.LoadedModel_Read();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var status = modelRuntime.LoadedModel_Read();
            return status with
            {
                ModelPath = request.ModelPath,
                Backend = request.Backend == ConfigurationBackend.Sglang ? "SGLang" : "vLLM",
                LoadLog = exception.Message,
                IsModelLoaded = false
            };
        }
    }

    public async Task<ModelLoadStatus> LoadDotLlmModelAsync(DotLlmLoadRequest request, CancellationToken cancellationToken = default)
    {
        await modelRuntime.LoadAsync(request, cancellationToken);
        return modelRuntime.LoadedModel_Read();
    }

    public async Task<ModelLoadStatus> UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        await modelRuntime.StopLlamaAsync(cancellationToken);
        return modelRuntime.LoadedModel_Read();
    }

    public async Task<ModelLoadStatus> UnloadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        await modelRuntime.UnloadLlamaAsync(modelPath, cancellationToken);
        return modelRuntime.LoadedModel_Read();
    }

    public async Task<ModelLoadStatus> UnloadModelAsync(string modelPath, ConfigurationBackend backend, CancellationToken cancellationToken = default)
    {
        await modelRuntime.UnloadAsync(modelPath, backend, cancellationToken);
        return modelRuntime.LoadedModel_Read();
    }

    #endregion

    #region BackendDiagnostics

    public Task<IReadOnlyList<BackendRuntimeStatus>> BackendRuntime_ReadAsync(CancellationToken cancellationToken = default) =>
        backendRuntimeInstaller is null
            ? Task.FromResult<IReadOnlyList<BackendRuntimeStatus>>([])
            : backendRuntimeInstaller.ReadAsync(cancellationToken);

    public async Task<BackendRuntimeStatus> BackendRuntime_CreateAsync(BackendRuntimeInstallRequest request, CancellationToken cancellationToken = default)
    {
        if (backendRuntimeInstaller is null)
            return new(request.PackageId, ConfigurationBackend.Llama, string.Empty, string.Empty, string.Empty, BackendRuntimeState.Failed,
                "Backend runtime installation is not configured.", false, true, DateTimeOffset.UtcNow);

        var package = await backendRuntimeInstaller.FindPackageByIdAsync(request.PackageId, cancellationToken).ConfigureAwait(false);
        if (package is null)
            return new(request.PackageId, ConfigurationBackend.Llama, string.Empty, string.Empty, string.Empty, BackendRuntimeState.Failed,
                "The selected backend runtime package is not available in the gallery.", false, true, DateTimeOffset.UtcNow);

        var installing = new BackendRuntimeStatus(package.Id, package.Backend, package.Route, package.RuntimeIdentifier, package.Version,
            BackendRuntimeState.Installing, "Downloading and verifying backend runtime...", false, package.RequiresRestart, DateTimeOffset.UtcNow);
        if (backendRuntimePublisher is not null)
            await backendRuntimePublisher.PublishCreateAsync(installing, cancellationToken).ConfigureAwait(false);

        var result = await backendRuntimeInstaller.InstallAsync(new BackendRuntimeInstallRequest(package.Id), cancellationToken).ConfigureAwait(false);
        if (backendRuntimePublisher is not null)
        {
            if (result.IsInstalled)
                await backendRuntimePublisher.PublishUpdateAsync(result, cancellationToken).ConfigureAwait(false);
            else
                await backendRuntimePublisher.PublishDeleteAsync(result, cancellationToken).ConfigureAwait(false);
        }

        if (result.IsInstalled)
            requirementMonitor?.RequestRefresh();
        return result;
    }

    public async Task<BackendRuntimeStatus> BackendRuntime_UpdateAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var status = (await BackendRuntime_ReadAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        if (status is null)
            return new(packageId, ConfigurationBackend.Llama, string.Empty, string.Empty, string.Empty, BackendRuntimeState.Failed,
                "The backend runtime package was not found.", false, true, DateTimeOffset.UtcNow);
        if (backendRuntimePublisher is not null)
            await backendRuntimePublisher.PublishUpdateAsync(status, cancellationToken).ConfigureAwait(false);
        return status;
    }

    public async Task BackendRuntime_DeleteAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var status = (await BackendRuntime_ReadAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        if (status is not null && backendRuntimePublisher is not null)
            await backendRuntimePublisher.PublishDeleteAsync(status, cancellationToken).ConfigureAwait(false);
    }

    public Task<OpenVinoDiagnosticsDto> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var result = openVinoDiagnostics.Diagnose();
        return Task.FromResult(new OpenVinoDiagnosticsDto
        {
            IsGpuReady = result.IsGpuReady,
            IsNpuReady = result.IsNpuReady,
            Devices = result.Devices.Select(device => new OpenVinoDeviceDto { Id = device.Id, Name = device.Name, IsCompatible = device.IsCompatible, Detail = device.Detail }).ToArray(),
            Checks = result.Checks.Select(check => new OpenVinoDiagnosticCheckDto { Id = check.Id, Name = check.Name, IsAvailable = check.IsAvailable, Detail = check.Detail, CanSolve = check.CanSolve }).ToArray(),
            Error = result.Error
        });
    }

    public async Task<OpenVinoSolveResultDto> SolveDiagnosticAsync(string checkId, CancellationToken cancellationToken = default)
    {
        var result = checkId switch
        {
            "level-zero-loader" or "intel-level-zero-gpu" => await openVinoInstaller.InstallAsync(cancellationToken),
            "render-permissions" => await openVinoInstaller.AddUserToRenderGroupsAsync(cancellationToken),
            _ => new OpenVinoInstallResult(false, "This diagnostic cannot be repaired automatically.", string.Empty)
        };
        return new OpenVinoSolveResultDto
        {
            Succeeded = result.Succeeded,
            Message = result.Message,
            Output = string.IsNullOrWhiteSpace(result.Output) ? "No installer output was returned by the server." : result.Output
        };
    }

    public async Task<BackendPrerequisiteDiagnostics> GetBackendPrerequisitesAsync(ConfigurationBackend backend, string pythonExecutable = "python3", CancellationToken cancellationToken = default, IReadOnlyList<string>? devices = null)
    {
        if (backend != ConfigurationBackend.OpenVino)
            return await (backendPrerequisites ?? new BackendPrerequisiteProvisioner()).DiagnoseAsync(backend, pythonExecutable, AppContext.BaseDirectory, cancellationToken: cancellationToken, devices: devices);

        var result = openVinoDiagnostics.Diagnose();
        var checks = result.Checks.Select(check => new BackendPrerequisiteCheck(check.Id, check.Name, check.IsAvailable, check.Detail, check.CanSolve)).ToArray();
        return new(backend, "OpenVINO", result.IsGpuReady || result.IsNpuReady, checks, result.Error);
    }

    public async Task<BackendPrerequisiteSolveResult> PrepareBackendAsync(ConfigurationBackend backend, string pythonExecutable = "python3", CancellationToken cancellationToken = default, IReadOnlyList<string>? devices = null)
    {
        if (backend == ConfigurationBackend.Llama)
        {
            var route = devices?.FirstOrDefault()?.Split(':', 2)[0] ?? "cpu";
            var package = backendRuntimeInstaller is null
                ? null
                : await backendRuntimeInstaller.FindPackageAsync(backend, route, cancellationToken).ConfigureAwait(false);
            if (package is null)
                return new(false, "No verified LLama runtime package is configured for this route.", "Configure a backend gallery package before installing this requirement.");

            var result = await BackendRuntime_CreateAsync(new BackendRuntimeInstallRequest(package.Id), cancellationToken).ConfigureAwait(false);
            return new(result.IsInstalled, result.Message, $"Package: {result.PackageId}{Environment.NewLine}Version: {result.Version}{Environment.NewLine}Route: {result.Route}");
        }

        if (backend is not (ConfigurationBackend.Vllm or ConfigurationBackend.Sglang))
            return new(false, "This backend has no user-space preparation action.", string.Empty);

        try
        {
            var result = await (backendPrerequisites ?? new BackendPrerequisiteProvisioner()).PrepareAsync(backend, pythonExecutable, AppContext.BaseDirectory, cancellationToken: cancellationToken, devices: devices);
            return new(true, result.Message, $"Python executable: {result.PythonExecutable}{Environment.NewLine}Requirements: {result.RequirementsPath}");
        }
        catch (Exception exception)
        {
            return new(false, exception.Message, exception.ToString());
        }
    }

    public async Task<OpenVinoLoadResultDto> LoadModelAsync(OpenVinoLoadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await modelRuntime.LoadAsync(request, cancellationToken);
            return new OpenVinoLoadResultDto(true, $"OpenVINO model loaded on {request.Device}.", request.Device);
        }
        catch (Exception exception)
        {
            return new OpenVinoLoadResultDto(false, exception.ToString(), request.Device);
        }
    }

    public Task<OpenVinoModelStatusDto> GetOpenVinoModelStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = modelRuntime.GetOpenVinoStatus();
        var modelSizeInBytes = status.ModelPath is not null && File.Exists(status.ModelPath)
            ? (ulong)new FileInfo(status.ModelPath).Length
            : 0;
        return Task.FromResult(new OpenVinoModelStatusDto(status.ModelPath, status.Device, status.IsModelLoaded, modelSizeInBytes, status.LoadLog));
    }

    #endregion

    #region ChatMessages

    public async Task<PersistedChat?> Chat_UpdateAsync(Guid id, ChatExchangeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.ModelPath))
            return null;
        var backend = ValidateChatBackend(request.Backend);
        var chat = await Chat_ReadAsync(id, cancellationToken);
        if (chat is null)
            return null;

        var generation = await effectiveInferenceService.GenerateAsync(chat, request, backend, cancellationToken: cancellationToken);
        return await Chat_UpdateCoreAsync(id, request.Content.Trim(), generation, request.ModelPath, backend, cancellationToken);
    }

    public async IAsyncEnumerable<ChatStreamUpdate> Chat_UpdateStreamAsync(
        Guid id,
        ChatExchangeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.ModelPath))
            yield break;

        var backend = ValidateChatBackend(request.Backend);
        var chat = await Chat_ReadAsync(id, cancellationToken);
        if (chat is null)
            yield break;

        var deltas = Channel.CreateUnbounded<string>();
        var generationTask = Task.Factory.StartNew(
            () => effectiveInferenceService.GenerateAsync(
                chat,
                request,
                backend,
                delta =>
                {
                    deltas.Writer.TryWrite(delta);
                    return Task.CompletedTask;
                },
                cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        _ = CompleteGenerationChannelAsync(generationTask, deltas.Writer);

        await foreach (var delta in deltas.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            if (!string.IsNullOrEmpty(delta))
                yield return new ChatStreamUpdate(id, delta);

        var generation = await generationTask.ConfigureAwait(false);
        var persistedChat = await Chat_UpdateCoreAsync(id, request.Content.Trim(), generation, request.ModelPath, backend, cancellationToken);
        if (persistedChat is not null)
            yield return new ChatStreamUpdate(id, string.Empty, true, persistedChat);
    }

    #endregion

    #region Helpers

    private static string ValidateChatBackend(string? backend)
    {
        var normalizedBackend = backend?.Trim();
        if (normalizedBackend is null ||
            (!string.Equals(normalizedBackend, "OpenVINO", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(normalizedBackend, "Vulkan", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(normalizedBackend, "CUDA", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(normalizedBackend, "SYCL", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(normalizedBackend, "CPU", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(normalizedBackend, "vLLM", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(normalizedBackend, "SGLang", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(normalizedBackend, "dotLLM", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unsupported chat backend '{backend}'.", nameof(backend));

        return normalizedBackend;
    }

    private static async Task CompleteGenerationChannelAsync(Task<GenerationResult> generationTask, ChannelWriter<string> writer)
    {
        try
        {
            await generationTask.ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    private static PersistedChat ToChat(ChatConversationEntity chat) => new(chat.Id, chat.Title, chat.CreatedAtUtc, chat.UpdatedAtUtc,
        chat.Messages.OrderBy(message => message.CreatedAtUtc).ThenBy(message => message.Id).Select(message => new PersistedChatMessage(message.Role, message.Content, message.CreatedAtUtc, message.ModelPath, message.Backend, message.TokenCount, message.TokensPerSecond)).ToArray());

    private static ModelSettings ToModelSettings(ModelSettingsEntity entity) =>
        new(entity.ModelPath, entity.Backend, entity.ConfigurationJson, entity.ConfigurationId);

    private static ModelConfiguration ToConfiguration(ModelConfigurationEntity entity) =>
        new(entity.Id, entity.Name, entity.Description, entity.ModelPath, entity.IsDefault, entity.SchemaVersion,
            entity.ConfigurationJson, entity.CreatedAtUtc, entity.UpdatedAtUtc, entity.Backend);

    private static T DeserializeConfiguration<T>(string configurationJson)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(configurationJson, ConfigurationJsonOptions)
                ?? throw new ArgumentException("The selected model configuration is empty.", nameof(configurationJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The selected model configuration is invalid.", nameof(configurationJson), exception);
        }
    }

    private static Model ToModel(ModelEntity entity) =>
        new(entity.Id, entity.Name, entity.Path, entity.SizeInBytes, entity.LastWriteTimeUtc, entity.ConfigurationId);

    private static void ValidateConfiguration(ModelConfiguration configuration)
    {
        if (!Enum.IsDefined(configuration.Backend))
            throw new ArgumentException("A valid configuration backend is required.", nameof(configuration));
        if (string.IsNullOrWhiteSpace(configuration.Name))
            throw new ArgumentException("A model configuration name is required.", nameof(configuration));
        if (string.IsNullOrWhiteSpace(configuration.ModelPath))
            throw new ArgumentException("A model path is required for a model configuration.", nameof(configuration));
        if (configuration.Name.Trim().Length > 120)
            throw new ArgumentException("A model configuration name cannot exceed 120 characters.", nameof(configuration));
        if (configuration.ConfigurationJson is null)
            throw new ArgumentException("Configuration JSON is required.", nameof(configuration));
        try
        {
            using var document = JsonDocument.Parse(configuration.ConfigurationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Configuration JSON must contain an object.", nameof(configuration));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Configuration JSON is invalid.", nameof(configuration), exception);
        }
    }

    private static async Task ClearOtherDefaultsAsync(
        ApplicationDbContext db,
        Guid selectedId,
        CancellationToken cancellationToken)
    {
        await db.ModelConfigurations
            .Where(item => item.Id != selectedId && item.IsDefault)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsDefault, false)
                .SetProperty(item => item.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
    }

    private static LlamaAdvancedSettings DeserializeAdvancedSettings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LlamaAdvancedSettings>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static IReadOnlyDictionary<string, VulkanDeviceSetting> DeserializeVulkanDevices(string json)
    {
        try
        {
            var devices = JsonSerializer.Deserialize<Dictionary<string, VulkanDeviceSetting?>>(json);
            if (devices is not null && devices.Values.All(setting => setting is not null))
                return devices.ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
        }

        var legacyWeights = JsonSerializer.Deserialize<Dictionary<string, float>>(json)
            ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        return legacyWeights.ToDictionary(
            pair => pair.Key,
            pair => new VulkanDeviceSetting(pair.Value > 0, Math.Max(0, pair.Value)),
            StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}