using Esi.AI.Models;

namespace Esi.AI.Studio.Data;

public sealed class BenchmarkRunEntity
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public ConfigurationBackend Backend { get; set; }

    public string BackendVariantId { get; set; } = string.Empty;

    public string ModelPath { get; set; } = string.Empty;

    public string ProfileSnapshotJson { get; set; } = "{}";

    public string SettingsJson { get; set; } = "{}";

    public string MeasurementsJson { get; set; } = "[]";

    public BenchmarkRunState State { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }
}
