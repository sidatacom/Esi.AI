using System.Text.Json;
using Esi.AI.Studio.Client.Services;
using Esi.AI.Studio.Data;
using Esi.AI.Llm.Chat;
using Microsoft.EntityFrameworkCore;

namespace Esi.AI.Studio.Services;

public sealed class DataService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ModelLibraryService modelLibrary) : IDataService
{
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
        return llamaModels;
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
                    UpdatedAtUtc = now
                });
            }
        }
        db.LlamaModels.RemoveRange(existing.Values.Where(model => !incomingPaths.Contains(model.Path)));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LlamaConfigurationProfile>> GetLlamaConfigurationProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.LlamaConfigurationProfiles.AsNoTracking()
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.Name)
            .ToArrayAsync(cancellationToken);
        return entities.Select(ToProfile).ToArray();
    }

    public async Task<LlamaConfigurationProfile?> GetLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.LlamaConfigurationProfiles.AsNoTracking()
            .Where(profile => profile.Id == id)
            .SingleOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToProfile(entity);
    }

    public async Task<LlamaConfigurationProfile> SaveLlamaConfigurationProfileAsync(LlamaConfigurationProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var duplicateName = await db.LlamaConfigurationProfiles.AnyAsync(item =>
            item.Name == profile.Name && item.Id != profile.Id, cancellationToken);
        if (duplicateName)
            throw new InvalidOperationException($"A configuration profile named '{profile.Name}' already exists.");

        var now = DateTime.UtcNow;
        var entity = profile.Id == Guid.Empty
            ? new LlamaConfigurationProfileEntity { Id = Guid.NewGuid(), CreatedAtUtc = now }
            : await db.LlamaConfigurationProfiles.SingleOrDefaultAsync(item => item.Id == profile.Id, cancellationToken)
                ?? throw new KeyNotFoundException("The configuration profile was not found.");
        entity.Name = profile.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(profile.Description) ? null : profile.Description.Trim();
        entity.ModelPath = profile.ModelPath.Trim();
        entity.IsDefault = profile.IsDefault;
        entity.SchemaVersion = profile.SchemaVersion < 1 ? 1 : profile.SchemaVersion;
        entity.ConfigurationJson = profile.ConfigurationJson;
        entity.UpdatedAtUtc = now;
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();
        if (db.Entry(entity).State == EntityState.Detached)
            db.LlamaConfigurationProfiles.Add(entity);

        if (entity.IsDefault)
            await ClearOtherDefaultsAsync(db, entity.Id, cancellationToken);
        else if (!await db.LlamaConfigurationProfiles.AnyAsync(item => item.IsDefault && item.Id != entity.Id, cancellationToken))
            entity.IsDefault = true;

        await db.SaveChangesAsync(cancellationToken);
        return ToProfile(entity);
    }

    public async Task DeleteLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.LlamaConfigurationProfiles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("The configuration profile was not found.");
        db.LlamaConfigurationProfiles.Remove(entity);
        if (entity.IsDefault)
        {
            var replacement = await db.LlamaConfigurationProfiles
                .Where(item => item.Id != id)
                .OrderBy(item => item.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (replacement is not null)
                replacement.IsDefault = true;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDefaultLlamaConfigurationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.LlamaConfigurationProfiles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
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

    public async Task<PersistedChat?> AddChatExchangeAsync(Guid id, string userContent, LlamaGenerationResult generation, string modelPath, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.ChatConversations.Include(item => item.Messages).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (chat is null) return null;
        var now = DateTime.UtcNow;
        chat.Messages.Add(new ChatMessageEntity { Role = "user", Content = userContent, CreatedAtUtc = now });
        chat.Messages.Add(new ChatMessageEntity { Role = "assistant", Content = generation.Text, ModelPath = modelPath, TokenCount = generation.TokenCount, TokensPerSecond = generation.TokensPerSecond, CreatedAtUtc = now });
        chat.UpdatedAtUtc = now;
        if (chat.Title == "Neuer Chat") chat.Title = userContent.Length > 60 ? userContent[..60] : userContent;
        await db.SaveChangesAsync(cancellationToken);
        return ToChat(chat);
    }

    private static PersistedChat ToChat(ChatConversationEntity chat) => new(chat.Id, chat.Title, chat.CreatedAtUtc, chat.UpdatedAtUtc,
        chat.Messages.OrderBy(message => message.CreatedAtUtc).ThenBy(message => message.Id).Select(message => new PersistedChatMessage(message.Role, message.Content, message.CreatedAtUtc, message.ModelPath, message.TokenCount, message.TokensPerSecond)).ToArray());

    private static LlamaSettings ToSettings(LlamaSettingsEntity entity) =>
        new(entity.ModelPath, entity.Backend, entity.GpuLayerCount, entity.ContextSize,
            DeserializeVulkanDevices(entity.VulkanDeviceWeightsJson),
            DeserializeAdvancedSettings(entity.AdvancedSettingsJson), entity.ConfigurationProfileId);

    private static LlamaConfigurationProfile ToProfile(LlamaConfigurationProfileEntity entity) =>
        new(entity.Id, entity.Name, entity.Description, entity.ModelPath, entity.IsDefault, entity.SchemaVersion,
            entity.ConfigurationJson, entity.CreatedAtUtc, entity.UpdatedAtUtc);

    private static LlamaModel ToModel(LlamaModelEntity entity) =>
        new(entity.Id, entity.Name, entity.Path, entity.SizeInBytes, entity.LastWriteTimeUtc);

    private static void ValidateProfile(LlamaConfigurationProfile profile)
    {
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
        await db.LlamaConfigurationProfiles
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