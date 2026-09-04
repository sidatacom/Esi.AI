using Esi.AI.Models;

namespace Esi.AI.Studio.Client.Services;

/// <summary>Central client-side snapshot for SignalR-backed collection state.</summary>
public interface IClientStateStore
{
    ModelLoadStatus LoadedModels { get; }

    IReadOnlyDictionary<Guid, ModelDownloadUpdate> Downloads { get; }

    BackendRequirementState BackendRequirements { get; }

    IReadOnlyDictionary<string, BackendRuntimeStatus> BackendRuntimes { get; }

    event Action? Changed;

    void ApplyLoadedModels(ModelLoadStatus status);

    void ApplyDownload(ModelDownloadUpdate update);

    void RemoveDownload(ModelDownloadUpdate update);

    void ApplyBackendRequirements(BackendRequirementState state);

    void ApplyBackendRuntime(BackendRuntimeStatus status);

    void RemoveBackendRuntime(BackendRuntimeStatus status);
}

/// <summary>Reconciles SignalR create/update/delete messages into one immutable-facing snapshot.</summary>
public sealed class ClientStateStore : IClientStateStore
{
    private readonly Dictionary<Guid, ModelDownloadUpdate> downloads = [];
    private readonly Dictionary<string, BackendRuntimeStatus> backendRuntimes = new(StringComparer.OrdinalIgnoreCase);

    public ModelLoadStatus LoadedModels { get; private set; } = new(null, string.Empty, 0, 0, 0, 0, [], null, string.Empty, new Dictionary<string, float>(), false, []);

    public IReadOnlyDictionary<Guid, ModelDownloadUpdate> Downloads => downloads;

    public BackendRequirementState BackendRequirements { get; private set; } = new([], DateTimeOffset.MinValue);

    public IReadOnlyDictionary<string, BackendRuntimeStatus> BackendRuntimes => backendRuntimes;

    public event Action? Changed;

    public void ApplyLoadedModels(ModelLoadStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        LoadedModels = status;
        NotifyChanged();
    }

    public void ApplyDownload(ModelDownloadUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        downloads[update.Download.Id] = update;
        NotifyChanged();
    }

    public void RemoveDownload(ModelDownloadUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        downloads.Remove(update.Download.Id);
        NotifyChanged();
    }

    public void ApplyBackendRequirements(BackendRequirementState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        BackendRequirements = state;
        NotifyChanged();
    }

    public void ApplyBackendRuntime(BackendRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        backendRuntimes[status.PackageId] = status;
        NotifyChanged();
    }

    public void RemoveBackendRuntime(BackendRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        backendRuntimes.Remove(status.PackageId);
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
