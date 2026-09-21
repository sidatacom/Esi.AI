using Esi.AI.Models;
using Esi.AI.Studio.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Services;

/// <summary>Owns the persisted application-wide settings used by server runtime policies.</summary>
public sealed class ApplicationSettingsService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<InferenceTimeoutOptions>? timeoutOptions = null,
    IOptions<ModelLibraryOptions>? modelLibraryOptions = null)
{
    private readonly InferenceTimeoutOptions defaults = timeoutOptions?.Value ?? new();
    private readonly ModelLibraryOptions modelLibraryDefaults = modelLibraryOptions?.Value ?? new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the current application settings or returns configured defaults on first use.</summary>
    public async Task<ApplicationSettings> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (entity is null || string.IsNullOrWhiteSpace(entity.InferenceTimeoutsJson))
            return CreateDefaults();

        try
        {
            return JsonSerializer.Deserialize<ApplicationSettings>(entity.InferenceTimeoutsJson, JsonOptions)
                ?? CreateDefaults();
        }
        catch (JsonException)
        {
            return CreateDefaults();
        }
    }

    /// <summary>Validates and persists application settings as the single global settings record.</summary>
    public async Task<ApplicationSettings> UpdateAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ApplicationSettings.SingleOrDefaultAsync(cancellationToken)
            ?? new ApplicationSettingsEntity { Id = 1 };
        entity.InferenceTimeoutsJson = JsonSerializer.Serialize(settings, JsonOptions);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        if (db.Entry(entity).State == EntityState.Detached)
            db.ApplicationSettings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    /// <summary>Seeds the central settings record once without overwriting existing user settings.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.ApplicationSettings.AnyAsync(cancellationToken))
            return;

        var defaults = CreateDefaults();
        db.ApplicationSettings.Add(new ApplicationSettingsEntity
        {
            Id = 1,
            InferenceTimeoutsJson = JsonSerializer.Serialize(defaults, JsonOptions),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private ApplicationSettings CreateDefaults() => new ApplicationSettings(
        InferenceTimeoutPolicy.BackendNames
            .Select(backend => defaults.Backends.TryGetValue(backend, out var configured)
                ? new InferenceTimeoutSettings(backend, configured.BaseSeconds, configured.SecondsPerTool, configured.SecondsPerPrefillToken)
                : new InferenceTimeoutSettings(backend, defaults.BaseSeconds, defaults.SecondsPerTool, defaults.SecondsPerPrefillToken))
            .ToArray(),
            BackendSandbox: new BackendSandboxSettings(),
            ModelLibrary: new ModelLibrarySettings(
                modelLibraryDefaults.Directories.Count > 0
                    ? modelLibraryDefaults.Directories
                    : [
                        "%USERPROFILE%/.cache/esi-ai/models",
                        "/home/llm/LocalAI/models",
                        "/home/llm/LocalAI/models/llama-cpp/models"],
                modelLibraryDefaults.SearchLimit,
                modelLibraryDefaults.MaxParallelDownloads,
                modelLibraryDefaults.MaxParallelFileDownloads,
                modelLibraryDefaults.HuggingFaceToken));

    private static void Validate(ApplicationSettings settings)
    {
        if (settings.InferenceTimeouts is null || settings.InferenceTimeouts.Count == 0 ||
            settings.InferenceTimeouts.Any(setting => string.IsNullOrWhiteSpace(setting.Backend) ||
                !double.IsFinite(setting.BaseSeconds) || setting.BaseSeconds < 0 ||
                !double.IsFinite(setting.SecondsPerTool) || setting.SecondsPerTool < 0 ||
                !double.IsFinite(setting.SecondsPerPrefillToken) || setting.SecondsPerPrefillToken < 0))
            throw new ArgumentException("Every backend requires finite, non-negative inference timeout values.", nameof(settings));

        var sandbox = settings.BackendSandbox ?? new BackendSandboxSettings();
        if (sandbox.CpuQuotaPercent <= 0 ||
            sandbox.TaskLimit <= 0 ||
            sandbox.DiagnosticTimeoutSeconds <= 0 ||
            sandbox.WorkerTimeoutSeconds <= 0 ||
            string.IsNullOrWhiteSpace(sandbox.MemoryLimitBytes))
            throw new ArgumentException("Backend sandbox limits must be positive and include a memory limit.", nameof(settings));
    }
}