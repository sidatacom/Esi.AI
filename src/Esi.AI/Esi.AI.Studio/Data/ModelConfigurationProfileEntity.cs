namespace Esi.AI.Studio.Data;

using Esi.AI.Models;

public sealed class ModelConfigurationProfileEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string ModelPath { get; set; } = string.Empty;

    public ConfigurationBackend Backend { get; set; } = ConfigurationBackend.Llama;

    public bool IsDefault { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public string ConfigurationJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}