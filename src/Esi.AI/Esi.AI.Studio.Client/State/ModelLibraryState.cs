using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class ModelLibraryState
{
    public List<LocalModel> Models { get; } = [];
    public List<string> Directories { get; } = [];
    public Dictionary<string, string> HuggingFaceIds { get; } = new(StringComparer.OrdinalIgnoreCase);
}