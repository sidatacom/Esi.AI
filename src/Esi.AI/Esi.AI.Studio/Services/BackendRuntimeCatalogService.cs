using System.Text.Json;
using System.Text.Json.Serialization;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Services;

/// <summary>Owns the SQLite-backed backend runtime catalog and its migration defaults.</summary>
public sealed class BackendRuntimeCatalogService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory) : IBackendRuntimeCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly BackendRuntimeOptions defaults = new();

    /// <inheritdoc />
    public async Task<BackendRuntimeOptions> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.BackendRuntimeSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return entity is null ? Clone(defaults) : FromEntity(entity);
    }

    /// <summary>Persists the complete backend runtime catalog as one consistent snapshot.</summary>
    public async Task<BackendRuntimeOptions> UpdateAsync(BackendRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.BackendRuntimeSettings.SingleOrDefaultAsync(cancellationToken)
            ?? new BackendRuntimeSettingsEntity { Id = 1 };
        entity.CatalogUrl = string.IsNullOrWhiteSpace(options.CatalogUrl) ? null : options.CatalogUrl.Trim();
        entity.InstallationDirectory = string.IsNullOrWhiteSpace(options.InstallationDirectory) ? null : options.InstallationDirectory.Trim();
        entity.AllowLocalPackages = options.AllowLocalPackages;
        entity.PackagesJson = JsonSerializer.Serialize(options.Packages, JsonOptions);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        if (db.Entry(entity).State == EntityState.Detached)
            db.BackendRuntimeSettings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return FromEntity(entity);
    }

    /// <summary>Copies configured JSON defaults into SQLite without overwriting user changes.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.BackendRuntimeSettings.AnyAsync(cancellationToken))
            return;

        db.BackendRuntimeSettings.Add(ToEntity(defaults));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static BackendRuntimeOptions FromEntity(BackendRuntimeSettingsEntity entity) => new()
    {
        CatalogUrl = entity.CatalogUrl,
        InstallationDirectory = entity.InstallationDirectory,
        AllowLocalPackages = entity.AllowLocalPackages,
        Packages = JsonSerializer.Deserialize<List<BackendRuntimePackage>>(entity.PackagesJson, JsonOptions) ?? []
    };

    private static BackendRuntimeSettingsEntity ToEntity(BackendRuntimeOptions options) => new()
    {
        CatalogUrl = options.CatalogUrl,
        InstallationDirectory = options.InstallationDirectory,
        AllowLocalPackages = options.AllowLocalPackages,
        PackagesJson = JsonSerializer.Serialize(options.Packages, JsonOptions),
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static BackendRuntimeOptions Clone(BackendRuntimeOptions options) => FromEntity(ToEntity(options));

    private static void Validate(BackendRuntimeOptions options)
    {
        if (options.Packages.Any(package => string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Version)))
            throw new ArgumentException("Every backend runtime package requires an id and version.", nameof(options));
    }
}
