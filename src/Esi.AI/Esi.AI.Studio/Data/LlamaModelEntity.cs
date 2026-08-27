namespace Esi.AI.Studio.Data;

public sealed class LlamaModelEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public long SizeInBytes { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid? ConfigurationProfileId { get; set; }
}