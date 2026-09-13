namespace Esi.AI.Studio.Data;

public sealed class BackendRuntimeSettingsEntity
{
    public int Id { get; set; } = 1;

    public string? CatalogUrl { get; set; }

    public string? InstallationDirectory { get; set; }

    public bool AllowLocalPackages { get; set; }

    public string PackagesJson { get; set; } = "[]";

    public DateTime UpdatedAtUtc { get; set; }
}
