using Esi.AI.Models;

namespace Esi.AI.Studio.Contracts;

/// <summary>
/// Publishes model download collection changes to connected clients.
/// </summary>
public interface IModelDownloadEvents
{
    event Func<IReadOnlyList<ModelDownloadUpdate>, Task>? ModelDownload_Read;
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Create;
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Update;
    event Func<ModelDownloadUpdate, Task>? ModelDownload_Delete;
}

/// <summary>
/// Publishes loaded model collection changes to connected clients.
/// </summary>
public interface IModelRuntimeEvents
{
    event Func<ModelLoadStatus, Task>? LoadedModel_Read;
    event Func<ModelLoadStatus, Task>? LoadedModel_Create;
    event Func<ModelLoadStatus, Task>? LoadedModel_Update;
    event Func<ModelLoadStatus, Task>? LoadedModel_Delete;
}

/// <summary>
/// Publishes backend prerequisite state changes to connected clients.
/// </summary>
public interface IBackendRequirementEvents
{
    event Func<BackendRequirementState, Task>? BackendRequirement_Read;
    event Func<BackendRequirementState, Task>? BackendRequirement_Update;
}

/// <summary>Publishes backend runtime installation collection changes to connected clients.</summary>
public interface IBackendRuntimeEvents
{
    event Func<IReadOnlyList<BackendRuntimeStatus>, Task>? BackendRuntime_Read;
    event Func<BackendRuntimeStatus, Task>? BackendRuntime_Create;
    event Func<BackendRuntimeStatus, Task>? BackendRuntime_Update;
    event Func<BackendRuntimeStatus, Task>? BackendRuntime_Delete;
}

/// <summary>Publishes changes to the global application settings snapshot.</summary>
public interface IApplicationSettingsEvents
{
    event Func<ApplicationSettings, Task>? ApplicationSettings_Update;
}

/// <summary>Publishes Vulkan runtime log collection changes to connected clients.</summary>
public interface IVulkanLogEvents
{
    event Func<IReadOnlyList<VulkanLogStatus>, Task>? VulkanLog_Read;
    event Func<VulkanLogStatus, Task>? VulkanLog_Create;
    event Func<VulkanLogStatus, Task>? VulkanLog_Update;
    event Func<VulkanLogStatus, Task>? VulkanLog_Delete;
}

/// <summary>Publishes training-run collection changes to connected local clients.</summary>
public interface ITrainingRunEvents
{
    event Func<IReadOnlyList<TrainingRunStatus>, Task>? TrainingRun_Read;
    event Func<TrainingRunStatus, Task>? TrainingRun_Create;
    event Func<TrainingRunStatus, Task>? TrainingRun_Update;
    event Func<TrainingRunStatus, Task>? TrainingRun_Delete;
}

/// <summary>Publishes transient provider communication entries to connected clients.</summary>
public interface IProviderTraceEvents
{
    event Func<ProviderTraceEntry, Task>? ProviderTrace_Create;
}

/// <summary>Publishes backend runtime installation changes from the server application layer.</summary>
public interface IBackendRuntimeStatusPublisher
{
    Task PublishCreateAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default);
    Task PublishUpdateAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default);
    Task PublishDeleteAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default);
}

/// <summary>Publishes Vulkan log CRUD changes over SignalR.</summary>
public interface IVulkanLogStatusPublisher
{
    Task PublishCreateAsync(VulkanLogStatus status, CancellationToken cancellationToken = default);
    Task PublishUpdateAsync(VulkanLogStatus status, CancellationToken cancellationToken = default);
    Task PublishDeleteAsync(VulkanLogStatus status, CancellationToken cancellationToken = default);
}