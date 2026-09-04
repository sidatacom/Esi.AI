using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Owns queued model downloads and their process-local lifecycle.</summary>
public interface IModelDownloadManager
{
    Task<Guid> StartDownloadAsync(string modelId, string? fileName, string library = "gguf", CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelDownloadOption>> GetDownloadOptionsAsync(string modelId, string library = "gguf", CancellationToken cancellationToken = default);

    Task PauseDownloadAsync(Guid downloadId, CancellationToken cancellationToken = default);

    Task ResumeDownloadAsync(Guid downloadId, CancellationToken cancellationToken = default);

    Task CancelDownloadAsync(Guid downloadId, CancellationToken cancellationToken = default);

    Task DeleteCompletedDownloadsAsync(CancellationToken cancellationToken = default);

    ModelDownloadStatus? GetDownload(Guid downloadId);

    IReadOnlyList<ModelDownloadStatus> GetDownloads();
}
