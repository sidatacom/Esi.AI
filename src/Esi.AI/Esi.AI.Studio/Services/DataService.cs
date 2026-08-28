using System.Text.Json;
using Esi.AI.Studio.Client.Services;
using Esi.AI.Studio.Data;
using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.EntityFrameworkCore;
using ClientModelLoadStatus = Esi.AI.Studio.Client.Services.ModelLoadStatus;
using ClientLoadedModelStatus = Esi.AI.Studio.Client.Services.LoadedModelStatus;
using ClientVulkanDeviceStatus = Esi.AI.Studio.Client.Services.VulkanDeviceStatus;

namespace Esi.AI.Studio.Services;

public sealed class DataService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ModelLibraryService modelLibrary,
    LlamaModelLoader llamaModelLoader,
    OpenVinoDiagnosticsService openVinoDiagnostics,
    OpenVinoDriverInstaller openVinoInstaller,
    OpenVinoModelLoader openVinoModelLoader) : IDataService
{
    public async Task<IReadOnlyList<LocalModel>> ScanLocalModelsAsync(CancellationToken cancellationToken = default) =>
        (await modelLibrary.ScanLocalModelsAsync(cancellationToken))
        .Select(model => new LocalModel(model.Name, model.Path, model.SizeInBytes, model.LastWriteTimeUtc)).ToArray();

    public IReadOnlyList<string> GetModelDirectories() => modelLibrary.GetModelDirectories();

    public Task<IReadOnlyList<string>> GetModelDirectoriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GetModelDirectories());

    public async Task<IReadOnlyList<HuggingFaceModel>> SearchModelsAsync(string query, CancellationToken cancellationToken = default) =>
        (await modelLibrary.SearchHuggingFaceAsync(query, cancellationToken))
        .Select(model => new HuggingFaceModel(model.Id, model.Author, model.Downloads, model.Likes, model.LastModified)).ToArray();

    public Task<Guid> StartModelDownloadAsync(ModelDownloadRequest request, CancellationToken cancellationToken = default) =>
        modelLibrary.StartDownloadAsync(request.ModelId, request.FileName, cancellationToken);

    public DownloadStatus? GetModelDownload(Guid id)
    {
        var status = modelLibrary.GetDownload(id);
        return status is null ? null : new DownloadStatus(status.Id, status.ModelId, status.FileName, status.DestinationPath,
            status.BytesDownloaded, status.TotalBytes, status.Completed, status.Error);
    }

    public Task<DownloadStatus?> GetModelDownloadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(GetModelDownload(id));

    public async Task<ModelStatus> SelectModelAsync(SelectModelRequest request, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(request.Path) || !request.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A fully qualified GGUF path is required.");
        var settings = await GetLlamaSettingsAsync(cancellationToken) ?? new LlamaSettings(
            request.Path, "Vulkan", 0, (uint)LlamaContextSize.Context128K,
            new Dictionary<string, VulkanDeviceSetting>(StringComparer.OrdinalIgnoreCase));
        await SaveLlamaSettingsAsync(settings with { ModelPath = request.Path }, cancellationToken);
        return new ModelStatus(request.Path, settings.Backend, settings.GpuLayerCount, settings.ContextSize, 0, false);
    }

    public Task<PersistedChat> CreateChatAsync(CreateChatRequest request, CancellationToken cancellationToken = default) =>
        CreateChatAsync(request.Title, cancellationToken);


    public async Task<LlamaSettings?> GetLlamaSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.LlamaSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToSettings(entity);
    }

    public async Task SaveLlamaSettingsAsync(LlamaSettings settings, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.LlamaSettings.SingleOrDefaultAsync(cancellationToken);
        entity ??= new LlamaSettingsEntity { Id = 1 };
        entity.ModelPath = settings.ModelPath;
        entity.Backend = settings.Backend;
        entity.GpuLayerCount = settings.GpuLayerCount;
        entity.ContextSize = settings.ContextSize;
        entity.VulkanDeviceWeightsJson = JsonSerializer.Serialize(settings.VulkanDevices);
        entity.AdvancedSettingsJson = JsonSerializer.Serialize(settings.Advanced ?? new());
        entity.ConfigurationProfileId = settings.ConfigurationProfileId;
        if (entity.Id == 1 && db.Entry(entity).State == EntityState.Detached)
            db.LlamaSettings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<OpenVinoSettings?> GetOpenVinoSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.OpenVinoSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (entity is null)
            return null;

        return JsonSerializer.Deserialize<OpenVinoSettings>(entity.SettingsJson)
            ?? throw new InvalidOperationException("The persisted OpenVINO settings are invalid.");
    }

    public async Task SaveOpenVinoSettingsAsync(OpenVinoSettings settings, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.OpenVinoSettings.SingleOrDefaultAsync(cancellationToken);
        entity ??= new OpenVinoSettingsEntity { Id = 1 };
        entity.SettingsJson = JsonSerializer.Serialize(settings);
        if (entity.Id == 1 && db.Entry(entity).State == EntityState.Detached)
            db.OpenVinoSettings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LlamaModel>> GetLlamaModelsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var models = await db.LlamaModels.AsNoTracking()
            .OrderBy(model => model.Name)
            .ToArrayAsync(cancellationToken);
        return models.Select(ToModel).ToArray();
    }

    public async Task<IReadOnlyList<LlamaModel>> ScanLlamaModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await modelLibrary.ScanLocalModelsAsync(cancellationToken);
        var llamaModels = models.Select(model => new LlamaModel(
            Guid.Empty, model.Name, model.Path, model.SizeInBytes, model.LastWriteTimeUtc)).ToArray();
        if (llamaModels.Length == 0)
            return await GetLlamaModelsAsync(cancellationToken);

        await SyncLlamaModelsAsync(llamaModels, cancellationToken);
        return await GetLlamaModelsAsync(cancellationToken);
    }

    public async Task SetModelConfigurationProfileAsync(string modelPath, Guid? profileId, CancellationToken cancellationToken = default)
    {
        var normalizedPath = modelPath.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.LlamaModels.SingleOrDefaultAsync(item => item.Path == normalizedPath, cancellationToken);
        if (model is null)
            return;

        if (profileId.HasValue)
        {
            var profile = await db.ModelConfigurationProfiles.SingleOrDefaultAsync(item => item.Id == profileId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("The model configuration profile was not found.");
            if (!string.Equals(profile.ModelPath.Trim(), normalizedPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The configuration profile does not belong to the selected model.", nameof(profileId));
        }

        model.ConfigurationProfileId = profileId;
        model.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncLlamaModelsAsync(IReadOnlyList<LlamaModel> models, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.LlamaModels.ToDictionaryAsync(model => model.Path, StringComparer.OrdinalIgnoreCase, cancellationToken);
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
                db.LlamaModels.Add(new LlamaModelEntity
                {
                    Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
                    Name = model.Name,
                    Path = model.Path,
                    SizeInBytes = model.SizeInBytes,
                    LastWriteTimeUtc = model.LastWriteTimeUtc,
                    UpdatedAtUtc = now,
                    ConfigurationProfileId = model.ConfigurationProfileId
                });
            }
        }
        db.LlamaModels.RemoveRange(existing.Values.Where(model => !incomingPaths.Contains(model.Path)));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ModelConfigurationProfile>> GetModelConfigurationProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.ModelConfigurationProfiles.AsNoTracking()
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.Name)
            .ToArrayAsync(cancellationToken);
        return entities.Select(ToProfile).ToArray();
    }

    public async Task<ModelConfigurationProfile?> GetModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelConfigurationProfiles.AsNoTracking()
            .Where(profile => profile.Id == id)
            .SingleOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToProfile(entity);
    }

    public async Task<ModelConfigurationProfile> SaveModelConfigurationProfileAsync(ModelConfigurationProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var duplicateName = await db.ModelConfigurationProfiles.AnyAsync(item =>
            item.Name == profile.Name && item.Backend == profile.Backend && item.Id != profile.Id, cancellationToken);
        if (duplicateName)
            throw new InvalidOperationException($"A configuration profile named '{profile.Name}' already exists.");

        var now = DateTime.UtcNow;
        var entity = profile.Id == Guid.Empty
            ? new ModelConfigurationProfileEntity { Id = Guid.NewGuid(), CreatedAtUtc = now }
            : await db.ModelConfigurationProfiles.SingleOrDefaultAsync(item => item.Id == profile.Id, cancellationToken)
                ?? throw new KeyNotFoundException("The configuration profile was not found.");
        entity.Name = profile.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(profile.Description) ? null : profile.Description.Trim();
        entity.ModelPath = profile.ModelPath.Trim();
        entity.Backend = profile.Backend;
        entity.IsDefault = profile.IsDefault;
        entity.SchemaVersion = profile.SchemaVersion < 1 ? 1 : profile.SchemaVersion;
        entity.ConfigurationJson = profile.ConfigurationJson;
        entity.UpdatedAtUtc = now;
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();
        if (db.Entry(entity).State == EntityState.Detached)
            db.ModelConfigurationProfiles.Add(entity);

        if (entity.IsDefault)
            await ClearOtherDefaultsAsync(db, entity.Id, cancellationToken);
        else if (!await db.ModelConfigurationProfiles.AnyAsync(item => item.IsDefault && item.Id != entity.Id, cancellationToken))
            entity.IsDefault = true;

        await db.SaveChangesAsync(cancellationToken);
        return ToProfile(entity);
    }

    public async Task DeleteModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelConfigurationProfiles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("The configuration profile was not found.");
        db.ModelConfigurationProfiles.Remove(entity);
        if (entity.IsDefault)
        {
            var replacement = await db.ModelConfigurationProfiles
                .Where(item => item.Id != id)
                .OrderBy(item => item.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (replacement is not null)
                replacement.IsDefault = true;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDefaultModelConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ModelConfigurationProfiles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("The configuration profile was not found.");
        await ClearOtherDefaultsAsync(db, id, cancellationToken);
        entity.IsDefault = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatSummary>> GetChatSummariesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ChatConversations.AsNoTracking().OrderByDescending(chat => chat.UpdatedAtUtc)
            .Select(chat => new ChatSummary(chat.Id, chat.Title, chat.UpdatedAtUtc, chat.Messages.Count)).ToArrayAsync(cancellationToken);
    }

    public async Task<PersistedChat> CreateChatAsync(string? title, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var chat = new ChatConversationEntity { Id = Guid.NewGuid(), Title = string.IsNullOrWhiteSpace(title) ? "Neuer Chat" : title.Trim(), CreatedAtUtc = now, UpdatedAtUtc = now };
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.ChatConversations.Add(chat);
        await db.SaveChangesAsync(cancellationToken);
        return ToChat(chat);
    }

    public async Task<PersistedChat?> GetChatAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.ChatConversations.AsNoTracking().Include(item => item.Messages).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return chat is null ? null : ToChat(chat);
    }

    public async Task<PersistedChat?> AddChatExchangeAsync(Guid id, string userContent, LlamaGenerationResult generation, string modelPath, string backend, CancellationToken cancellationToken = default) =>
        await AddChatExchangeAsync(id, userContent, generation.Text, modelPath, backend, generation.TokenCount, generation.TokensPerSecond, cancellationToken);

    private async Task<PersistedChat?> AddChatExchangeAsync(Guid id, string userContent, string assistantContent, string modelPath, string backend, int? tokenCount, double? tokensPerSecond, CancellationToken cancellationToken = default)
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

    public Task<ClientModelLoadStatus> GetModelStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ToClientStatus(llamaModelLoader.GetStatus()));

    public async Task<ClientModelLoadStatus> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ModelPath) ||
            !string.Equals(Path.GetExtension(request.ModelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("LLama loading requires a .gguf model path.", nameof(request));

        var advanced = request.Advanced;
        await llamaModelLoader.LoadAsync(request.ModelPath, request.Backend, request.GpuLayerCount, request.ContextSize,
            request.VulkanDeviceWeights,
            new LlamaLoadOptions(advanced.MainGpu, advanced.SeqMax, advanced.RecurrentRollbackSnapshots, advanced.UseMemorymap,
                advanced.UseDirectIO, advanced.UseMemoryLock, advanced.Threads, advanced.BatchThreads, advanced.BatchSize,
                advanced.UBatchSize, advanced.Embeddings, advanced.NoKqvOffload, advanced.FlashAttention, advanced.VocabOnly,
                advanced.OpOffload, advanced.SwaFull, advanced.KVUnified, advanced.RopeFrequencyBase, advanced.RopeFrequencyScale,
                advanced.YarnExtrapolationFactor, advanced.YarnAttentionFactor, advanced.YarnBetaFast, advanced.YarnBetaSlow,
                advanced.YarnOriginalContext), cancellationToken);
        return ToClientStatus(llamaModelLoader.GetStatus());
    }

    public async Task<ClientModelLoadStatus> UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        await llamaModelLoader.StopAsync(cancellationToken);
        return ToClientStatus(llamaModelLoader.GetStatus());
    }

    public async Task<ClientModelLoadStatus> UnloadModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        await llamaModelLoader.UnloadAsync(modelPath, cancellationToken);
        return ToClientStatus(llamaModelLoader.GetStatus());
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

    public async Task<OpenVinoLoadResultDto> LoadModelAsync(OpenVinoLoadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await openVinoModelLoader.LoadAsync(
                request.ModelPath,
                request.Device,
                cancellationToken,
                new OpenVinoGenerationOptions(request.MaxNewTokens, request.Temperature, request.TopP, request.DoSample, request.TopK, request.RepetitionPenalty),
                request.CacheDirectory,
                new OpenVinoNpuOptions(
                    request.Npu?.MaxPromptLength ?? 1024,
                    request.Npu?.MinResponseLength ?? 128,
                    request.Npu?.PrefillHint ?? "DYNAMIC",
                    request.Npu?.GenerateHint ?? "FAST_COMPILE"));
            return new OpenVinoLoadResultDto(true, $"OpenVINO model loaded on {request.Device}.", request.Device);
        }
        catch (Exception exception)
        {
            return new OpenVinoLoadResultDto(false, exception.ToString(), request.Device);
        }
    }

    public Task<OpenVinoModelStatusDto> GetOpenVinoModelStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = openVinoModelLoader.GetStatus();
        var modelSizeInBytes = status.ModelPath is not null && File.Exists(status.ModelPath)
            ? (ulong)new FileInfo(status.ModelPath).Length
            : 0;
        return Task.FromResult(new OpenVinoModelStatusDto(status.ModelPath, status.Device, status.IsModelLoaded, modelSizeInBytes));
    }

    public async Task<PersistedChat?> AddChatExchangeAsync(Guid id, ChatExchangeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.ModelPath))
            return null;
        var backend = request.Backend?.Trim();
        if (!string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backend, "Vulkan", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(backend, "CPU", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported chat backend '{request.Backend}'.", nameof(request));
        var chat = await GetChatAsync(id, cancellationToken);
        if (chat is null)
            return null;

        if (string.Equals(backend, "OpenVINO", StringComparison.OrdinalIgnoreCase))
        {
            var modelPath = Path.GetFullPath(request.ModelPath);
            var openVinoStatus = openVinoModelLoader.GetStatus();
            if (!openVinoStatus.IsModelLoaded || !string.Equals(openVinoStatus.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected OpenVINO model is not loaded.");

            using var openVinoSession = openVinoModelLoader.CreateChatSession();
            var openVinoGeneration = openVinoSession.GenerateWithStats(request.Content.Trim());
            return await AddChatExchangeAsync(id, request.Content.Trim(), openVinoGeneration.Text, modelPath, "OpenVINO", openVinoGeneration.TokenCount, openVinoGeneration.TokensPerSecond, cancellationToken);
        }

        if (!string.Equals(Path.GetExtension(request.ModelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("LLama chat requires a .gguf model path.", nameof(request));

        using var session = llamaModelLoader.CreateChatSession("You are a helpful assistant.", request.ModelPath);
        var messages = chat.Messages.Select(message => new LlamaChatMessage(message.Role, message.Content))
            .Append(new LlamaChatMessage("user", request.Content.Trim())).ToArray();
        var generation = await session.GenerateWithStatsAsync(messages, cancellationToken);
        return await AddChatExchangeAsync(id, request.Content.Trim(), generation, request.ModelPath, backend!, cancellationToken);
    }

    private static ClientModelLoadStatus ToClientStatus(Esi.AI.Core.ModelLoading.ModelLoadStatus status) =>
        new(status.ModelPath, status.Backend, status.GpuLayerCount, status.ContextSize, status.ModelSizeInBytes,
            status.FoundVulkanGpuCount,
            status.VulkanDevices.Select(device => new ClientVulkanDeviceStatus(device.Name, device.Description, device.AssignedLayerCount, device.ModelBufferMiB)).ToArray(),
            status.CpuModelBufferMiB, status.LoadLog, status.VulkanDeviceWeights, status.IsModelLoaded,
            status.LoadedModels.Select(model => new ClientLoadedModelStatus(model.ModelPath, model.Backend, model.GpuLayerCount, model.ContextSize, model.ModelSizeInBytes,
                model.VulkanDevices.Select(device => new ClientVulkanDeviceStatus(device.Name, device.Description, device.AssignedLayerCount, device.ModelBufferMiB)).ToArray(), model.CpuModelBufferMiB)).ToArray());

    private static PersistedChat ToChat(ChatConversationEntity chat) => new(chat.Id, chat.Title, chat.CreatedAtUtc, chat.UpdatedAtUtc,
        chat.Messages.OrderBy(message => message.CreatedAtUtc).ThenBy(message => message.Id).Select(message => new PersistedChatMessage(message.Role, message.Content, message.CreatedAtUtc, message.ModelPath, message.Backend, message.TokenCount, message.TokensPerSecond)).ToArray());

    private static LlamaSettings ToSettings(LlamaSettingsEntity entity) =>
        new(entity.ModelPath, entity.Backend, entity.GpuLayerCount, entity.ContextSize,
            DeserializeVulkanDevices(entity.VulkanDeviceWeightsJson),
            DeserializeAdvancedSettings(entity.AdvancedSettingsJson), entity.ConfigurationProfileId);

    private static ModelConfigurationProfile ToProfile(ModelConfigurationProfileEntity entity) =>
        new(entity.Id, entity.Name, entity.Description, entity.ModelPath, entity.IsDefault, entity.SchemaVersion,
            entity.ConfigurationJson, entity.CreatedAtUtc, entity.UpdatedAtUtc, entity.Backend);

    private static LlamaModel ToModel(LlamaModelEntity entity) =>
        new(entity.Id, entity.Name, entity.Path, entity.SizeInBytes, entity.LastWriteTimeUtc, entity.ConfigurationProfileId);

    private static void ValidateProfile(ModelConfigurationProfile profile)
    {
        if (!Enum.IsDefined(profile.Backend))
            throw new ArgumentException("A valid configuration backend is required.", nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("A configuration profile name is required.", nameof(profile));
        if (profile.Name.Trim().Length > 120)
            throw new ArgumentException("A configuration profile name cannot exceed 120 characters.", nameof(profile));
        if (profile.ConfigurationJson is null)
            throw new ArgumentException("Configuration JSON is required.", nameof(profile));
        try
        {
            using var document = JsonDocument.Parse(profile.ConfigurationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Configuration JSON must contain an object.", nameof(profile));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Configuration JSON is invalid.", nameof(profile), exception);
        }
    }

    private static async Task ClearOtherDefaultsAsync(
        ApplicationDbContext db,
        Guid selectedId,
        CancellationToken cancellationToken)
    {
        await db.ModelConfigurationProfiles
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
}