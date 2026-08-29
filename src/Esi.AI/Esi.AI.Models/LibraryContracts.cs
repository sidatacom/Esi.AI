namespace Esi.AI.Models;

public sealed record LocalModel(string Name, string Path, long SizeInBytes, DateTime LastWriteTimeUtc, ReferenceModelFormat Format = ReferenceModelFormat.Gguf);

public sealed record HuggingFaceModel(string Id, string? Author, long Downloads, long Likes, DateTime? LastModified);

public sealed record DownloadStatus(Guid Id, string ModelId, string FileName, string DestinationPath, long BytesDownloaded, long? TotalBytes, bool Completed, string? Error, bool Paused = false, bool Queued = false, IReadOnlyList<DownloadFileStatus>? Files = null)
{
    public double Percent => TotalBytes is > 0 ? BytesDownloaded * 100d / TotalBytes.Value : Completed ? 100 : 0;
}

public sealed record DownloadFileStatus(string FileName, long BytesDownloaded, long? TotalBytes, bool Completed)
{
    public double Percent => TotalBytes is > 0 ? BytesDownloaded * 100d / TotalBytes.Value : Completed ? 100 : 0;
}

public sealed record ModelDownloadUpdate(DownloadStatus Download, IReadOnlyList<LocalModel>? LocalModels = null);

public sealed record DownloadStarted(Guid Id);

public sealed record ModelStatus(string? ModelPath, string Backend, int GpuLayerCount, uint ContextSize, int FoundVulkanGpuCount, bool IsModelLoaded);
