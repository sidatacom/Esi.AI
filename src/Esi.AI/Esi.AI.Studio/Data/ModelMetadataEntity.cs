namespace Esi.AI.Studio.Data;

public sealed class ModelMetadataEntity
{
    public Guid Id { get; set; }

    public string ModelPath { get; set; } = string.Empty;

    public string CompatibleBackendsJson { get; set; } = "[]";

    public string? HuggingFaceModelId { get; set; }

    public string? HuggingFaceRevision { get; set; }

    public DateTime? HuggingFaceSynchronizedAtUtc { get; set; }

    public bool IsManuallyConfigured { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}