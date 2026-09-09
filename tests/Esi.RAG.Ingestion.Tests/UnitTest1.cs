using Esi.RAG.Application;
using Esi.RAG.Domain;
using Esi.RAG.Ingestion;

namespace Esi.RAG.Ingestion.Tests;

public class UnitTest1
{
    [Fact]
    public async Task IngestionService_Should_Index_Discovered_Files()
    {
        var service = new IngestionService(
            new FakeDiscovery(),
            new FakeExtractor(),
            new FakeChunker(),
            new FakeEmbeddingClient(),
            new FakeVectorStore());

        var result = await service.IngestAsync(new IngestionRequest("repo"), CancellationToken.None);
        Assert.Equal(1, result.FilesDiscovered);
        Assert.Equal(1, result.FilesIndexed);
        Assert.Equal(1, result.SegmentsExtracted);
        Assert.Equal(1, result.ChunksIndexed);
    }

    private sealed class FakeDiscovery : IRepositoryDiscovery
    {
        public Task<RepositoryDiscoveryResult> DiscoverAsync(string repositoryPath, CancellationToken cancellationToken)
        {
            var document = new RepositoryDocument("repo\\a.cs", "a.cs", ".cs", "csharp", 10, "hash", DateTimeOffset.UtcNow, false);
            var project = new ProjectMetadata("repo", "repo", null, null, null, DateTimeOffset.UtcNow);
            return Task.FromResult(new RepositoryDiscoveryResult([document], [], project));
        }
    }

    private sealed class FakeExtractor : ISourceExtractor
    {
        public Task<ExtractionResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult(new ExtractionResult(filePath, "csharp", [new SourceSegment(filePath, "csharp", "method:M", "line1", 1, 1)]));
    }

    private sealed class FakeChunker : IChunker
    {
        public IReadOnlyList<SourceSegment> Chunk(ExtractionResult extractionResult) => extractionResult.Segments;
    }

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        public Task<IReadOnlyList<float>> CreateEmbeddingAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<float>>([1f, 0f]);
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<RetrievedPassage>> SearchAsync(IReadOnlyList<float> queryEmbedding, int limit, SearchFilter? filter, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RetrievedPassage>>([]);

        public Task<int> DeleteByFilePathAsync(string relativePath, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}

