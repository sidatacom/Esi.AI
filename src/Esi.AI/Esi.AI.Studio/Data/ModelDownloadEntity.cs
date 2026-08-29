namespace Esi.AI.Studio.Data;

public sealed class ModelDownloadEntity
{
    public Guid Id { get; set; }

    public string ModelId { get; set; } = string.Empty;

    public string Library { get; set; } = string.Empty;

    public string DestinationPath { get; set; } = string.Empty;

    public string Revision { get; set; } = string.Empty;

    public string FileNamesJson { get; set; } = "[]";

    public string FileStatusesJson { get; set; } = "[]";

    public bool Paused { get; set; }

    public bool Completed { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}