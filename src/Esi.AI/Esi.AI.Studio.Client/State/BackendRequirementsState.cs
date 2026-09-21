using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>SignalR state for backend prerequisite requirements.</summary>
public sealed class BackendRequirementsState
{
    public BackendRequirementState Snapshot { get; private set; } = new([], DateTimeOffset.MinValue);
    internal void Read(BackendRequirementState state) => Snapshot = state;
    internal void Create(BackendRequirementState state) => Snapshot = state;
    internal void Update(BackendRequirementState state) => Snapshot = state;
    internal void Delete(BackendRequirementState state) => Snapshot = state with { Entries = [] };
}