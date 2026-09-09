namespace Esi.RAG.Domain;

// ---- Repository / project metadata --------------------------------------

/// <summary>Safe, execution-free snapshot of repository identity metadata.</summary>
public sealed record ProjectMetadata(
    string RootPath,
    string RepositoryName,
    string? GitBranch,
    string? GitCommitSha,
    string? GitRemoteUrl,
    DateTimeOffset DiscoveredAtUtc);

/// <summary>A single discovered file, prior to extraction/chunking.</summary>
public sealed record RepositoryDocument(
    string AbsolutePath,
    string RelativePath,
    string Extension,
    string Language,
    long SizeBytes,
    string ContentHash,
    DateTimeOffset LastModifiedUtc,
    bool IsGenerated);

// ---- Extraction -----------------------------------------------------------

public sealed record SourceSegment(
    string FilePath,
    string Language,
    string Symbol,
    string Content,
    int StartLine,
    int EndLine);

/// <summary>A Roslyn-derived (or fallback) code symbol discovered during extraction.</summary>
public sealed record CodeSymbol(
    string Kind,
    string Name,
    string? Namespace,
    string? ContainingType,
    string FilePath,
    int StartLine,
    int EndLine,
    string Signature);

/// <summary>A SQL object (table/view/procedure/function) or interesting statement discovered during extraction.</summary>
public sealed record SqlObject(
    string ObjectType,
    string Name,
    string? Schema,
    string FilePath,
    int StartLine,
    int EndLine,
    string DefinitionExcerpt);

public sealed record ExtractionResult(
    string FilePath,
    string Language,
    IReadOnlyList<SourceSegment> Segments,
    IReadOnlyList<CodeSymbol>? Symbols = null,
    IReadOnlyList<SqlObject>? SqlObjects = null);

// ---- Chunking / indexing ---------------------------------------------------

/// <summary>An embedded, indexable slice of a source file with a stable, reproducible identity.</summary>
public sealed record DocumentChunk(
    string Id,
    string RepositoryName,
    string? CommitSha,
    string FilePath,
    string RelativePath,
    string Language,
    string Symbol,
    string Content,
    string ContentHash,
    int StartLine,
    int EndLine,
    int ChunkIndex,
    IReadOnlyList<float> Embedding,
    string EmbeddingModel = "",
    string IngestionFingerprint = "",
    int FileChunkCount = 0,
    string FileContentHash = "");

// ---- Search / retrieval -----------------------------------------------------

public sealed record SearchFilter(
    IReadOnlyCollection<string>? Languages = null,
    IReadOnlyCollection<string>? PathPrefixes = null,
    IReadOnlyCollection<string>? SymbolPrefixes = null,
    bool ExcludeGenerated = true);

public sealed record SearchQuery(
    string Text,
    int Limit,
    SearchFilter? Filter = null,
    float MinScore = 0f);

public sealed record RetrievedPassage(DocumentChunk Chunk, float Score);

public sealed record Citation(
    string FilePath,
    int StartLine,
    int EndLine,
    string Excerpt,
    string RelativePath = "",
    string Symbol = "",
    string Language = "",
    float Score = 0f,
    string? CommitSha = null);

// ---- Agent / investigation --------------------------------------------------

public sealed record InvestigationPlan(string Goal, IReadOnlyList<string> PlannedSteps);

public sealed record InvestigationStep(
    int Index,
    string Description,
    string ToolName,
    string ToolInput,
    string Output,
    IReadOnlyList<Citation> Citations,
    bool Success);

public sealed record InvestigationResult(
    string Goal,
    IReadOnlyList<InvestigationStep> Steps,
    IReadOnlyList<Citation> Citations,
    string Answer,
    bool EvidenceSufficient);

/// <summary>Mutable-by-replacement state carried across bounded agent rounds.</summary>
public sealed record AgentState(
    string Goal,
    InvestigationPlan Plan,
    IReadOnlyList<InvestigationStep> CompletedSteps,
    IReadOnlyList<Citation> Citations,
    bool IsComplete);

// Kept for extractive, single-shot search responses (used inside InvestigationStep.Output composition).
public sealed record AskStep(
    string Prompt,
    string Finding,
    IReadOnlyList<Citation> Citations);

public sealed record AskResult(
    string Query,
    IReadOnlyList<AskStep> Steps,
    string Answer);

// ---- Ingestion reporting -----------------------------------------------------

public sealed record IngestionIssue(string FilePath, string Reason);

public sealed record IngestionReport(
    string RepositoryPath,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int FilesDiscovered,
    int FilesIndexed,
    int SegmentsExtracted,
    int ChunksIndexed,
    IReadOnlyList<IngestionIssue> SkippedFiles,
    IReadOnlyList<IngestionIssue> Errors,
    ProjectMetadata? Project,
    int FilesSkipped = 0,
    int FilesFailed = 0,
    int ChunksEmbedded = 0,
    int ChunksUploaded = 0);
