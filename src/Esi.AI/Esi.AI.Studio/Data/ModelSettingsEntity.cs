using Esi.AI.Models;

namespace Esi.AI.Studio.Data;

public sealed class ModelSettingsEntity
{
    public int Id { get; set; }

    public string ModelPath { get; set; } = string.Empty;

    public ConfigurationBackend Backend { get; set; }

    public string BackendVariantId { get; set; } = string.Empty;

    public string ConfigurationJson { get; set; } = "{}";

    public List<Device> Devices { get; set; } = new List<Device>();

    public Guid? ConfigurationId { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}