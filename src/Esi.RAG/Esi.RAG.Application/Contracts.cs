using Esi.RAG.Domain;

namespace Esi.RAG.Application;

// ---- Requests / responses ---------------------------------------------------

public sealed record IngestionRequest(string RepositoryPath, IProgress<IngestionProgress>? Progress = null);

public sealed record IngestionProgress(
    int FilesProcessed,
    int TotalFiles,
    int FilesSkipped,
    int FilesFailed,
    int ChunksEmbedded,
    int ChunksUploaded,
    TimeSpan Elapsed,
    string? CurrentFile);

public sealed record SearchRequest(string Query, int Limit = 5, SearchFilter? Filter = null);

public sealed record SearchResult(string Answer, IReadOnlyList<Citation> Citations);

public sealed record AskRequest(string Query, int MaxRounds = 3, int MaxCitations = 12, SearchFilter? Filter = null);

public sealed record ChunkingOptions(int MaxLinesPerChunk, int OverlapLines);

public sealed class IngestionOptions
{
    public int EmbeddingConcurrency { get; init; } = 2;
    public int UpsertBatchSize { get; init; } = 256;
    public int ChunkMaxLines { get; init; } = 80;
    public int ChunkOverlapLines { get; init; } = 12;

    public int ValidatedEmbeddingConcurrency => Math.Max(1, EmbeddingConcurrency);
    public int ValidatedUpsertBatchSize => Math.Clamp(UpsertBatchSize, 128, 512);
}

public sealed record HealthResult(string Status, string DefaultRepositoryPath, string VectorStore, string EmbeddingProvider);

public sealed record ComponentHealthResult(string Component, bool Healthy, string Detail);

// ---- Ports

public interface IRepositoryDiscovery
{
    Task<RepositoryDiscoveryResult> DiscoverAsync(string repositoryPath, CancellationToken cancellationToken);
}

public sealed record RepositoryDiscoveryResult(
    IReadOnlyList<RepositoryDocument> Documents,
    IReadOnlyList<IngestionIssue> Skipped,
    ProjectMetadata Project);

public interface ISourceExtractor
{
    Task<ExtractionResult> ExtractAsync(string filePath, CancellationToken cancellationToken);
}

public interface IChunker
{
    IReadOnlyList<SourceSegment> Chunk(ExtractionResult extractionResult);
}

public interface IEmbeddingClient
{
    string ModelName => string.Empty;

    Task<IReadOnlyList<float>> CreateEmbeddingAsync(string text, CancellationToken cancellationToken);
}

public interface ITextGenerationClient
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

public sealed record FileIndexMetadata(
    string RelativePath,
    string? CommitSha,
    string ContentHash,
    string EmbeddingModel,
    string IngestionFingerprint,
    int ChunkCount);

public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);

    Task<FileIndexMetadata?> GetFileIndexMetadataAsync(string relativePath, CancellationToken cancellationToken)
        => Task.FromResult<FileIndexMetadata?>(null);

    Task<IReadOnlyList<RetrievedPassage>> SearchAsync(IReadOnlyList<float> queryEmbedding, int limit, SearchFilter? filter, CancellationToken cancellationToken);

    Task<int> DeleteByFilePathAsync(string relativePath, CancellationToken cancellationToken);
}

// ---- Application services -----------------------------------------------------

public interface IIngestionService
{
    Task<IngestionReport> IngestAsync(IngestionRequest request, CancellationToken cancellationToken);
}

public interface ISearchService
{
    Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}

public interface IAskService
{
    Task<InvestigationResult> AskAsync(AskRequest request, CancellationToken cancellationToken);
}

public interface IHealthService
{
    Task<HealthResult> CheckAsync(CancellationToken cancellationToken);

    Task<ComponentHealthResult> CheckQdrantAsync(CancellationToken cancellationToken);

    Task<ComponentHealthResult> CheckLmStudioAsync(CancellationToken cancellationToken);
}
