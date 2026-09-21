using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>SignalR state for the loaded-model collection.</summary>
public sealed class ActiveModelsSignalRState
{
    public ModelLoadStatus Snapshot { get; private set; } = new(null, string.Empty, 0, 0, 0, 0, [], null, string.Empty, new Dictionary<string, float>(), false, []);
    public IReadOnlyList<LoadedModelStatus> Loaded => Snapshot.LoadedModels;
    internal void Read(ModelLoadStatus status) => Snapshot = status;
    internal void Create(ModelLoadStatus status) => Snapshot = status;
    internal void Update(ModelLoadStatus status) => Snapshot = status;
    internal void Delete(ModelLoadStatus status) => Snapshot = status;
}