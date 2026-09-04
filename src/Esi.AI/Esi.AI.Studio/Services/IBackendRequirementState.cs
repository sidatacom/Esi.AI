using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Exposes cached backend requirement state and requests asynchronous refreshes.</summary>
public interface IBackendRequirementState
{
    BackendRequirementState Current { get; }

    void RequestRefresh();
}
