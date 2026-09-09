using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.Extensions.Options;
using Esi.RAG.Ingestion;

namespace Esi.RAG.Ingestion.Tests;

public sealed class ResumableIngestionTests
{
    [Fact]
    public async Task Uploads_completed_batches_before_repository_finishes()
    {
        var store = new RecordingVectorStore();
        var service = CreateService(store, new FixedChunker(129));

        var report = await service.IngestAsync(new IngestionRequest("repo"), CancellationToken.None);

        Assert.Equal(2, store.UpsertCalls.Count);
        Assert.Equal([128, 1], store.UpsertCalls.Select(batch => batch.Count).ToArray());
        Assert.Equal(129, report.ChunksUploaded);
    }

    [Fact]
    public async Task Skips_only_when_the_stored_version_matches()
    {
        var store = new RecordingVectorStore
        {
            Metadata = new FileIndexMetadata("a.cs", "commit", "file-hash", "model", Fingerprint(), 1)
        };
        var service = CreateService(store, new FixedChunker(1), new FixedEmbeddingClient(), "file-hash", "model");

        var report = await service.IngestAsync(new IngestionRequest("repo"), CancellationToken.None);

        Assert.Equal(1, report.FilesSkipped);
        Assert.Empty(store.UpsertCalls);
        Assert.Empty(store.DeletedPaths);
    }

    [Fact]
    public async Task Reindexes_changed_files_and_deletes_stale_points()
    {
        var store = new RecordingVectorStore
        {
            Metadata = new FileIndexMetadata("a.cs", "commit", "old-hash", "model", Fingerprint(), 1)
        };
        var service = CreateService(store, new FixedChunker(1), new FixedEmbeddingClient(), "new-hash", "model");

        await service.IngestAsync(new IngestionRequest("repo"), CancellationToken.None);

        Assert.Equal(["a.cs"], store.DeletedPaths);
        Assert.Single(store.UpsertCalls);
    }

    [Fact]
    public async Task Embedding_concurrency_is_bounded()
    {
        var embedding = new TrackingEmbeddingClient();
        var service = CreateService(new RecordingVectorStore(), new FixedChunker(8), embedding, "file-hash", "model", 2);

        await service.IngestAsync(new IngestionRequest("repo"), CancellationToken.None);

        Assert.True(embedding.MaxConcurrency <= 2);
        Assert.True(embedding.MaxConcurrency > 1);
    }

    [Fact]
    public async Task Reports_progress_and_honors_cancellation()
    {
        var progress = new List<IngestionProgress>();
        var service = CreateService(new RecordingVectorStore(), new FixedChunker(1), new FixedEmbeddingClient());

        await service.IngestAsync(new IngestionRequest("repo", new Progress<IngestionProgress>(progress.Add)), CancellationToken.None);

        Assert.Contains(progress, value => value.CurrentFile == "a.cs");
        Assert.Contains(progress, value => value.ChunksUploaded == 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.IngestAsync(new IngestionRequest("repo"), cancellation.Token));
    }

    private static IngestionService CreateService(
        RecordingVectorStore store,
        IChunker chunker,
        IEmbeddingClient? embedding = null,
        string fileHash = "file-hash",
        string model = "model",
        int concurrency = 2)
        => new(
            new FixedDiscovery(fileHash),
            new FixedExtractor(),
            chunker,
            embedding ?? new FixedEmbeddingClient(model),
            store,
            Options.Create(new IngestionOptions
            {
                EmbeddingConcurrency = concurrency,
                UpsertBatchSize = 128,
                ChunkMaxLines = 80,
                ChunkOverlapLines = 12
            }));

    private static string Fingerprint()
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("model=model|max-lines=80|overlap=12"))).ToLowerInvariant();

    private sealed class FixedDiscovery(string fileHash) : IRepositoryDiscovery
    {
        public Task<RepositoryDiscoveryResult> DiscoverAsync(string repositoryPath, CancellationToken cancellationToken)
            => Task.FromResult(new RepositoryDiscoveryResult(
                [new RepositoryDocument("repo\\a.cs", "a.cs", ".cs", "csharp", 10, fileHash, DateTimeOffset.UtcNow, false)],
                [],
                new ProjectMetadata("repo", "repo", "main", "commit", null, DateTimeOffset.UtcNow)));
    }

    private sealed class FixedExtractor : ISourceExtractor
    {
        public Task<ExtractionResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult(new ExtractionResult(filePath, "csharp", [new SourceSegment(filePath, "csharp", "class:A", "content", 1, 1)]));
    }

    private sealed class FixedChunker(int count) : IChunker
    {
        public IReadOnlyList<SourceSegment> Chunk(ExtractionResult extractionResult)
            => Enumerable.Range(0, count).Select(index => new SourceSegment(extractionResult.FilePath, "csharp", $"chunk:{index}", $"content:{index}", index + 1, index + 1)).ToArray();
    }

    private sealed class FixedEmbeddingClient(string model = "model") : IEmbeddingClient
    {
        public string ModelName => model;
        public Task<IReadOnlyList<float>> CreateEmbeddingAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<float>>([1f, 0f]);
    }

    private sealed class TrackingEmbeddingClient : IEmbeddingClient
    {
        private int _active;
        public int MaxConcurrency { get; private set; }
        public string ModelName => "model";

        public async Task<IReadOnlyList<float>> CreateEmbeddingAsync(string text, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            MaxConcurrency = Math.Max(MaxConcurrency, active);
            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref _active);
            return [1f, 0f];
        }
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public FileIndexMetadata? Metadata { get; init; }
        public List<IReadOnlyList<DocumentChunk>> UpsertCalls { get; } = [];
        public List<string> DeletedPaths { get; } = [];

        public Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpsertCalls.Add(chunks.ToArray());
            return Task.CompletedTask;
        }

        public Task<FileIndexMetadata?> GetFileIndexMetadataAsync(string relativePath, CancellationToken cancellationToken)
            => Task.FromResult(Metadata);

        public Task<IReadOnlyList<RetrievedPassage>> SearchAsync(IReadOnlyList<float> queryEmbedding, int limit, SearchFilter? filter, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RetrievedPassage>>([]);

        public Task<int> DeleteByFilePathAsync(string relativePath, CancellationToken cancellationToken)
        {
            DeletedPaths.Add(relativePath);
            return Task.FromResult(0);
        }
    }
}
