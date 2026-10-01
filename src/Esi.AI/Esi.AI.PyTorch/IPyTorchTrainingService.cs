using Esi.AI.Models;

namespace Esi.AI.PyTorch;

/// <summary>Manages the server-owned lifecycle of local XPU QLoRA training runs.</summary>
public interface IPyTorchTrainingService
{
    /// <summary>Creates a pending training run and starts its Python worker.</summary>
    /// <param name="request">The validated training inputs.</param>
    /// <param name="cancellationToken">A token that cancels creation before the worker starts.</param>
    /// <returns>The initial pending run status.</returns>
    Task<TrainingRunStatus> TrainingRun_CreateAsync(CreateTrainingRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the current server-owned training run collection.</summary>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The current training run statuses.</returns>
    Task<IReadOnlyList<TrainingRunStatus>> TrainingRun_ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads and republishes the current status of one training run.</summary>
    /// <param name="id">The training run identifier.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The current status, or <see langword="null" /> when the run is absent.</returns>
    Task<TrainingRunStatus?> TrainingRun_UpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Cancels an active run or removes a completed run.</summary>
    /// <param name="id">The training run identifier.</param>
    /// <param name="cancellationToken">A token that cancels waiting for worker shutdown.</param>
    Task TrainingRun_DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a local copy of the packaged sample JSONL dataset.</summary>
    /// <param name="cancellationToken">A token that cancels the file copy.</param>
    /// <returns>The absolute path to the copied dataset.</returns>
    Task<string> TrainingRun_SampleDataset_CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Publishes training run collection changes without coupling the trainer to SignalR.</summary>
public interface ITrainingRunStatusPublisher
{
    /// <summary>Publishes a newly created training run.</summary>
    Task PublishCreateAsync(TrainingRunStatus status, CancellationToken cancellationToken = default);

    /// <summary>Publishes a changed training run.</summary>
    Task PublishUpdateAsync(TrainingRunStatus status, CancellationToken cancellationToken = default);

    /// <summary>Publishes a removed, cancelled, or failed training run.</summary>
    Task PublishDeleteAsync(TrainingRunStatus status, CancellationToken cancellationToken = default);
}