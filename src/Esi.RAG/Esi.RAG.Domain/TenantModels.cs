namespace Esi.RAG.Domain;

public sealed record TenantContext(string TenantId, IReadOnlySet<string> Roles, IReadOnlySet<string> Permissions)
{
    public static TenantContext Create(string tenantId, IEnumerable<string>? roles = null, IEnumerable<string>? permissions = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        }

        return new TenantContext(
            tenantId.Trim(),
            (roles ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
            (permissions ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}

public enum TenantSourceType
{
    Sql,
    SharePoint,
    OneDrive,
}

public sealed record TenantSourceConfiguration(
    TenantSourceType SourceType,
    string SourceId,
    bool Enabled,
    IReadOnlyDictionary<string, string> Settings);

public sealed record TenantConfiguration(
    string TenantId,
    string SqlConnectionString,
    IReadOnlyList<TenantSourceConfiguration> Sources);

public sealed record TenantDataItem(
    string TenantId,
    TenantSourceType SourceType,
    string SourceId,
    string SourcePath,
    string Content,
    DateTimeOffset LastModifiedUtc,
    string ContentHash,
    IReadOnlySet<string> AllowedRoles,
    IReadOnlySet<string> AllowedPermissions);

public sealed record TenantSourceBatch(
    IReadOnlyList<TenantDataItem> Items,
    IReadOnlySet<string> DeletedSourcePaths,
    string? ChangeToken,
    string? Cursor);

public sealed record TenantDataDocument(
    string Id,
    string TenantId,
    TenantSourceType SourceType,
    string SourceId,
    string SourcePath,
    string Content,
    DateTimeOffset LastModifiedUtc,
    string ContentHash,
    IReadOnlySet<string> AllowedRoles,
    IReadOnlySet<string> AllowedPermissions,
    IReadOnlyList<float> Embedding,
    string EmbeddingModel,
    string IngestionFingerprint,
    string? ChangeToken = null);

public sealed record TenantSyncState(
    string TenantId,
    TenantSourceType SourceType,
    string SourceId,
    string? ChangeToken,
    DateTimeOffset? LastSuccessfulSyncUtc,
    string? Cursor);

public sealed record TenantSyncProgress(
    string TenantId,
    TenantSourceType SourceType,
    string SourceId,
    int ItemsDiscovered,
    int ItemsIndexed,
    int ItemsSkipped,
    int ItemsDeleted,
    string? CurrentItem);

public sealed record TenantSyncReport(
    string TenantId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int ItemsDiscovered,
    int ItemsIndexed,
    int ItemsSkipped,
    int ItemsDeleted,
    IReadOnlyList<IngestionIssue> Errors);
