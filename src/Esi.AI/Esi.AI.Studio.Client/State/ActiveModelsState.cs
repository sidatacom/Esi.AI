using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>Reusable view of the currently loaded models.</summary>
public sealed class ActiveModelsState
{
    private readonly ActiveModelsSignalRState source;

    internal ActiveModelsState(ActiveModelsSignalRState source) => this.source = source;

    public ModelLoadStatus Snapshot => source.Snapshot;
    public IReadOnlyList<LoadedModelStatus> Loaded => source.Loaded;
}