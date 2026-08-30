using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Publishes CRUD changes for the loaded-model collection.</summary>
public interface IModelRuntimeStatusPublisher
{
    /// <summary>Publishes creation of a pending loaded-model item.</summary>
    Task LoadedModel_CreateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default);

    /// <summary>Publishes an update to the loaded-model collection.</summary>
    Task LoadedModel_UpdateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default);

    /// <summary>Publishes deletion of a pending or unloaded loaded-model item.</summary>
    Task LoadedModel_DeleteAsync(ModelLoadStatus status, CancellationToken cancellationToken = default);
}