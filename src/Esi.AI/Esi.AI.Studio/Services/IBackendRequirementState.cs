using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Exposes cached backend requirement state and requests asynchronous refreshes.</summary>
public interface IBackendRequirementState
{
    BackendRequirementState Current { get; }

    Task<BackendRequirementState> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Refreshes only the specified backend variants.</summary>
    Task<BackendRequirementState> RefreshAsync(IReadOnlyCollection<string> backendIds, CancellationToken cancellationToken = default);

    void RequestRefresh();

    /// <summary>Queues a refresh for one backend variant.</summary>
    void RequestRefresh(string backendId);

    /// <summary>Queues refreshes for backend variants matching a family and device routes.</summary>
    void RequestRefresh(ConfigurationBackend backend, IReadOnlyCollection<string>? devices);
}
