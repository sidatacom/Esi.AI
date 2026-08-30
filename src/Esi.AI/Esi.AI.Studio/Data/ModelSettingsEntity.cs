using Esi.AI.Models;

namespace Esi.AI.Studio.Data;

public sealed class ModelSettingsEntity
{
    public int Id { get; set; }

    public string ModelPath { get; set; } = string.Empty;

    public ConfigurationBackend Backend { get; set; }

    public string ConfigurationJson { get; set; } = "{}";

    public Guid? ConfigurationId { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}