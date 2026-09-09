using System.Security.Cryptography;
using System.Text;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Esi.RAG.Ingestion;

/// <summary>Indexes one repository file at a time and resumes from completed file versions.</summary>
public sealed class IngestionService(
    IRepositoryDiscovery repositoryDiscovery,
    ISourceExtractor sourceExtractor,
    IChunker chunker,
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    IOptions<IngestionOptions>? options = null) : IIngestionService
{
    private readonly IngestionOptions _options = options?.Value ?? new IngestionOptions();

    public async Task<IngestionReport> IngestAsync(IngestionRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var discovery = await repositoryDiscovery.DiscoverAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
        var errors = new List<IngestionIssue>();
        var skipped = new List<IngestionIssue>(discovery.Skipped);
        var pendingUploads = new List<DocumentChunk>(_options.ValidatedUpsertBatchSize);
        var totalFiles = discovery.Documents.Count + discovery.Skipped.Count;
        var filesProcessed = discovery.Skipped.Count;
        var filesIndexed = 0;
        var filesSkipped = discovery.Skipped.Count;
        var filesFailed = 0;
        var segmentsExtracted = 0;
        var chunksEmbedded = 0;
        var chunksUploaded = 0;
        var fingerprint = ComputeFingerprint(_options, embeddingClient.ModelName);

        void Report(string? currentFile) => request.Progress?.Report(new IngestionProgress(
            filesProcessed, totalFiles, filesSkipped, filesFailed, chunksEmbedded, chunksUploaded,
            DateTimeOffset.UtcNow - startedAt, currentFile));

        Report(null);
        foreach (var document in discovery.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(document.RelativePath);

            ExtractionResult extraction;
            IReadOnlyList<SourceSegment> segmentedChunks;
            try
            {
                extraction = await sourceExtractor.ExtractAsync(document.AbsolutePath, cancellationToken).ConfigureAwait(false);
                segmentedChunks = chunker.Chunk(extraction);
                segmentsExtracted += extraction.Segments.Count;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                filesFailed++;
                errors.Add(new IngestionIssue(document.RelativePath, ex.Message));
                filesProcessed++;
                Report(null);
                continue;
            }

            var existing = await vectorStore.GetFileIndexMetadataAsync(document.RelativePath, cancellationToken).ConfigureAwait(false);
            if (existing is not null &&
                existing.CommitSha == discovery.Project.GitCommitSha &&
                string.Equals(existing.ContentHash, document.ContentHash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.EmbeddingModel, embeddingClient.ModelName, StringComparison.Ordinal) &&
                string.Equals(existing.IngestionFingerprint, fingerprint, StringComparison.Ordinal) &&
                existing.ChunkCount == segmentedChunks.Count)
            {
                skipped.Add(new IngestionIssue(document.RelativePath, "unchanged"));
                filesSkipped++;
                filesProcessed++;
                Report(null);
                continue;
            }

            // Embedding failures intentionally escape this method so callers can retry safely.
            var embeddedChunks = await EmbedChunksAsync(
                segmentedChunks, discovery.Project, document, fingerprint, cancellationToken).ConfigureAwait(false);
            chunksEmbedded += embeddedChunks.Count;

            try
            {
                await vectorStore.DeleteByFilePathAsync(document.RelativePath, cancellationToken).ConfigureAwait(false);
                pendingUploads.AddRange(embeddedChunks);
                filesIndexed++;
                filesProcessed++;

                while (pendingUploads.Count >= _options.ValidatedUpsertBatchSize)
                {
                    var batch = pendingUploads.GetRange(0, _options.ValidatedUpsertBatchSize);
                    pendingUploads.RemoveRange(0, batch.Count);
                    await vectorStore.UpsertAsync(batch, cancellationToken).ConfigureAwait(false);
                    chunksUploaded += batch.Count;
                    Report(null);
                }

                Report(null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                filesFailed++;
                errors.Add(new IngestionIssue(document.RelativePath, ex.Message));
                Report(null);
            }
        }

        if (pendingUploads.Count > 0)
        {
            await vectorStore.UpsertAsync(pendingUploads, cancellationToken).ConfigureAwait(false);
            chunksUploaded += pendingUploads.Count;
            pendingUploads.Clear();
        }

        Report(null);
        return new IngestionReport(
            request.RepositoryPath,
            startedAt,
            DateTimeOffset.UtcNow,
            discovery.Documents.Count + discovery.Skipped.Count,
            filesIndexed,
            segmentsExtracted,
            chunksUploaded,
            skipped,
            errors,
            discovery.Project,
            filesSkipped,
            filesFailed,
            chunksEmbedded,
            chunksUploaded);
    }

    private async Task<IReadOnlyList<DocumentChunk>> EmbedChunksAsync(
        IReadOnlyList<SourceSegment> segments,
        ProjectMetadata project,
        RepositoryDocument document,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var results = new DocumentChunk?[segments.Count];
        await Parallel.ForEachAsync(
            segments.Select((segment, index) => (segment, index)),
            new ParallelOptions { MaxDegreeOfParallelism = _options.ValidatedEmbeddingConcurrency, CancellationToken = cancellationToken },
            async (item, ct) =>
            {
                var embedding = await embeddingClient.CreateEmbeddingAsync(item.segment.Content, ct).ConfigureAwait(false);
                results[item.index] = new DocumentChunk(
                    ComputeStableId(project.RepositoryName, project.GitCommitSha, document.RelativePath, item.segment.StartLine, item.segment.EndLine, item.index),
                    project.RepositoryName,
                    project.GitCommitSha,
                    item.segment.FilePath,
                    document.RelativePath,
                    item.segment.Language,
                    item.segment.Symbol,
                    item.segment.Content,
                    ComputeHash(item.segment.Content),
                    item.segment.StartLine,
                    item.segment.EndLine,
                    item.index,
                    embedding,
                    embeddingClient.ModelName,
                    fingerprint,
                    segments.Count,
                    document.ContentHash);
            }).ConfigureAwait(false);

        return results.Select(result => result!).ToArray();
    }

    private static string ComputeFingerprint(IngestionOptions options, string modelName)
    {
        var value = $"model={modelName}|max-lines={options.ChunkMaxLines}|overlap={options.ChunkOverlapLines}";
        return ComputeHash(value);
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeStableId(string repositoryName, string? commitSha, string relativePath, int startLine, int endLine, int chunkIndex)
    {
        var seed = $"{repositoryName}|{commitSha}|{relativePath}|{startLine}|{endLine}|{chunkIndex}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash[..16]).ToString();
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddRagIngestion(this IServiceCollection services)
    {
        services.AddSingleton<IIngestionService, IngestionService>();
        return services;
    }
}
