namespace Esi.AI.Models;

/// <summary>Represents one transient communication step through the local provider stack.</summary>
public sealed record ProviderTraceEntry(
    Guid Id,
    string RequestId,
    DateTimeOffset Timestamp,
    string Layer,
    string Direction,
    string Title,
    string Detail,
    string? Payload = null);
