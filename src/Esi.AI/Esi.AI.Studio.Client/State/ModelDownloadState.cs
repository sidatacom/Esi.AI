using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class ModelDownloadState
{
    public Dictionary<Guid, DownloadStatus> Items { get; } = [];
    public Dictionary<string, ModelsDownloadOptions> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
}