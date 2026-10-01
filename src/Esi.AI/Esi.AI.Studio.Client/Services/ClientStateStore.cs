using Esi.AI.Models;
using Esi.AI.Studio.Client.State;

namespace Esi.AI.Studio.Client.Services;

/// <summary>Central client-side snapshot for SignalR-backed collection state.</summary>
public interface IClientStateStore
{
    SignalRClientState State { get; }

    ActiveModelsSignalRState ActiveModels { get; }

    DownloadsState Downloads { get; }

    BackendRequirementsState BackendRequirements { get; }

    BackendRuntimesState BackendRuntimes { get; }

    VulkanLogsState VulkanLogs { get; }

    TrainingRunsState TrainingRuns { get; }

    event Action? Changed;

    void LoadedModel_Read(ModelLoadStatus status);
    void LoadedModel_Create(ModelLoadStatus status);
    void LoadedModel_Update(ModelLoadStatus status);
    void LoadedModel_Delete(ModelLoadStatus status);

    void ModelDownload_Read(IEnumerable<ModelDownloadUpdate> updates);
    void ModelDownload_Create(ModelDownloadUpdate update);
    void ModelDownload_Update(ModelDownloadUpdate update);
    void ModelDownload_Delete(ModelDownloadUpdate update);

    void BackendRequirement_Read(BackendRequirementState state);
    void BackendRequirement_Update(BackendRequirementState state);

    void BackendRuntime_Read(IEnumerable<BackendRuntimeStatus> statuses);
    void BackendRuntime_Create(BackendRuntimeStatus status);
    void BackendRuntime_Update(BackendRuntimeStatus status);
    void BackendRuntime_Delete(BackendRuntimeStatus status);

    void VulkanLog_Read(IEnumerable<VulkanLogStatus> statuses);
    void VulkanLog_Create(VulkanLogStatus status);
    void VulkanLog_Update(VulkanLogStatus status);
    void VulkanLog_Delete(VulkanLogStatus status);

    void TrainingRun_Read(IEnumerable<TrainingRunStatus> statuses);
    void TrainingRun_Create(TrainingRunStatus status);
    void TrainingRun_Update(TrainingRunStatus status);
    void TrainingRun_Delete(TrainingRunStatus status);

}

/// <summary>Reconciles SignalR create/update/delete messages into one immutable-facing snapshot.</summary>
public sealed class ClientStateStore : IClientStateStore
{
    public SignalRClientState State { get; } = new();
    public ActiveModelsSignalRState ActiveModels => State.ActiveModels;
    public DownloadsState Downloads => State.Downloads;
    public BackendRequirementsState BackendRequirements => State.BackendRequirements;
    public BackendRuntimesState BackendRuntimes => State.BackendRuntimes;
    public VulkanLogsState VulkanLogs => State.VulkanLogs;
    public TrainingRunsState TrainingRuns => State.TrainingRuns;

    public event Action? Changed;

    public void LoadedModel_Read(ModelLoadStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        ActiveModels.Read(status);
        NotifyChanged();
    }

    public void LoadedModel_Create(ModelLoadStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        ActiveModels.Create(status);
        NotifyChanged();
    }

    public void LoadedModel_Update(ModelLoadStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        ActiveModels.Update(status);
        NotifyChanged();
    }

    public void LoadedModel_Delete(ModelLoadStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        ActiveModels.Delete(status);
        NotifyChanged();
    }

    public void ModelDownload_Read(IEnumerable<ModelDownloadUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        Downloads.Read(updates);
        NotifyChanged();
    }

    public void ModelDownload_Create(ModelDownloadUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Downloads.Create(update);
        NotifyChanged();
    }

    public void ModelDownload_Update(ModelDownloadUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Downloads.Update(update);
        NotifyChanged();
    }

    public void ModelDownload_Delete(ModelDownloadUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Downloads.Delete(update);
        NotifyChanged();
    }

    public void BackendRequirement_Read(BackendRequirementState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        BackendRequirements.Read(state);
        NotifyChanged();
    }

    public void BackendRequirement_Update(BackendRequirementState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        BackendRequirements.Update(state);
        NotifyChanged();
    }

    public void BackendRuntime_Read(IEnumerable<BackendRuntimeStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        BackendRuntimes.Read(statuses);
        NotifyChanged();
    }

    public void BackendRuntime_Create(BackendRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        BackendRuntimes.Create(status);
        NotifyChanged();
    }

    public void BackendRuntime_Update(BackendRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        BackendRuntimes.Update(status);
        NotifyChanged();
    }

    public void BackendRuntime_Delete(BackendRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        BackendRuntimes.Delete(status);
        NotifyChanged();
    }

    public void VulkanLog_Read(IEnumerable<VulkanLogStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        VulkanLogs.Read(statuses);
        NotifyChanged();
    }

    public void VulkanLog_Create(VulkanLogStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        VulkanLogs.Create(status);
        NotifyChanged();
    }

    public void VulkanLog_Update(VulkanLogStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        VulkanLogs.Update(status);
        NotifyChanged();
    }

    public void VulkanLog_Delete(VulkanLogStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        VulkanLogs.Delete(status);
        NotifyChanged();
    }

    public void TrainingRun_Read(IEnumerable<TrainingRunStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        TrainingRuns.Read(statuses);
        NotifyChanged();
    }

    public void TrainingRun_Create(TrainingRunStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        TrainingRuns.Create(status);
        NotifyChanged();
    }

    public void TrainingRun_Update(TrainingRunStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        TrainingRuns.Update(status);
        NotifyChanged();
    }

    public void TrainingRun_Delete(TrainingRunStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        TrainingRuns.Delete(status);
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
