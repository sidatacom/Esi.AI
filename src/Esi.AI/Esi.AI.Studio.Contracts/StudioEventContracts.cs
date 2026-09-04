using Esi.AI.Models;

namespace Esi.AI.Studio.Contracts;

/// <summary>
/// Publishes model download collection changes to connected clients.
/// </summary>
public interface IModelDownloadEvents
{
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Create;
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Update;
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Delete;
}

/// <summary>
/// Publishes loaded model collection changes to connected clients.
/// </summary>
public interface IModelRuntimeEvents
{
    event Func<ModelLoadStatus, Task>? LoadedModel_Create;
    event Func<ModelLoadStatus, Task>? LoadedModel_Update;
    event Func<ModelLoadStatus, Task>? LoadedModel_Delete;
}

/// <summary>
/// Publishes backend prerequisite state changes to connected clients.
/// </summary>
public interface IBackendRequirementEvents
{
    event Func<BackendRequirementState, Task>? BackendRequirementStateUpdated;
}

/// <summary>Publishes backend runtime installation collection changes to connected clients.</summary>
public interface IBackendRuntimeEvents
{
    event Func<BackendRuntimeStatus, Task>? BackendRuntime_Create;
    event Func<BackendRuntimeStatus, Task>? BackendRuntime_Update;
    event Func<BackendRuntimeStatus, Task>? BackendRuntime_Delete;
}

/// <summary>Publishes backend runtime installation changes from the server application layer.</summary>
public interface IBackendRuntimeStatusPublisher
{
    Task PublishCreateAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default);
    Task PublishUpdateAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default);
    Task PublishDeleteAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default);
}