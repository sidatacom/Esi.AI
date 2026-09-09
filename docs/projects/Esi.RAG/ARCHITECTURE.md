# Esi.RAG architecture

The solution is split into six projects:

- **Domain** contains immutable, rich domain records: `ProjectMetadata`, `RepositoryDocument`,
  `SourceSegment`, `CodeSymbol`, `SqlObject`, `DocumentChunk`, `SearchQuery`/`SearchFilter`,
  `Citation`, `InvestigationPlan`/`Step`/`Result`, `AgentState`, and `IngestionReport`.
- **Application** contains ports (`IRepositoryDiscovery`, `ISourceExtractor`, `IChunker`,
  `IEmbeddingClient`, `ITextGenerationClient`, `IVectorStore`, `IHealthService`, ...), request/result
  contracts, and the retrieval/agent orchestration (`SearchService`, `InvestigationAgent`) that only
  depends on those ports - no direct dependency on Qdrant/LM Studio/Roslyn.
- **Infrastructure** implements discovery (with size/secret/generated-content exclusion), a safe
  execution-free Git metadata reader, Roslyn/SQL/Markdown extraction, chunking, the typed LM Studio
  HTTP client (retries/timeouts/health), and the real Qdrant HTTP adapter (collection management,
  payload upsert/search/delete, filtering, health) with in-memory fallback.
- **Ingestion** coordinates discovery -> extraction -> chunking -> embedding -> indexing and produces
  an `IngestionReport` (files discovered/indexed/skipped, segments, chunks, errors, project metadata).
- **Api** exposes `/health`, `/health/qdrant`, `/health/lmstudio`, `/api/ingestion/index`,
  `/api/search`, `/api/ask`, `/api/tenant/sync`, and `/api/tenant/search`.
- **Cli** (built with `System.CommandLine`) provides the same operations for scripts and local use.
- **Tenant data flow** is implemented through tenant contracts in Domain/Application, orchestration in Ingestion, and isolated Qdrant/in-memory stores in Infrastructure. It is separate from the repository/code flow.

## Tenant data architecture

The code collection remains project-scoped. Tenant documents use `TenantDataDocument` and are written to a collection formed from `TenantRag:CollectionPrefix` plus the authenticated tenant ID. `TenantQdrantVectorStore` also stores the tenant ID in payload and applies it as a Qdrant filter and an application-side validation.

`ITenantContextAccessor` is the security boundary: tenant synchronization and search must obtain context from authentication, never from a query parameter. `ITenantConfigurationResolver` is the integration point for the host application's MultiTenant `EsiDbContext`. `ITenantSqlSource` and `ITenantGraphSource` are explicit source ports; the Graph port is shared by SharePoint and OneDrive. Source batches carry changed items, deleted paths, cursors, and change tokens. State is keyed by tenant, source type, and source ID.

The repository currently supplies fail-loud placeholder adapters because the referenced MultiTenant context and Graph abstraction are owned by the host application. Register host implementations before enabling tenant synchronization. Detailed integration guidance is in [`tenant-data.md`](tenant-data.md).

## Safety properties

- The source repository is opened read-only; discovery never executes anything from it.
- Git metadata (branch/commit/remote) is read by parsing `.git/HEAD`, `.git/refs/**`,
  `.git/packed-refs`, and `.git/config` as plain text - `git.exe` is never invoked and no
  repository code or hooks are executed.
- Discovery excludes configured directory names, enforces a maximum file size, skips
  filenames/content matching configured secret patterns, and skips filenames/content matching
  configured generated-file markers. Skipped files are reported with a reason code, not silently
  dropped.
- Chunk identity is a deterministic UUID derived from repository name + commit sha + relative path
  + line range + chunk index, so re-ingesting unchanged content upserts the same points instead of
  duplicating them.
- The investigation agent (`InvestigationAgent`) has exactly one tool - bounded repository search -
  and a fixed round/citation budget; it never executes code, writes files, or calls arbitrary tools,
  and it is instructed to answer only from retrieved evidence.

## Extraction and fallback behavior

C# symbols are extracted with Roslyn, qualified by namespace and containing type; files with fatal
syntax errors (or no recognized symbols) fall back to a single whole-file segment so no source is
silently lost. Markdown files are split into heading sections while preserving original line
numbers. SQL extraction keeps per-statement line bounds and additionally recognizes
`CREATE`/`ALTER TABLE|VIEW|PROCEDURE|FUNCTION` statements as `SqlObject` metadata. Chunk line numbers
remain relative to the original source segment.

## Retrieval

Search over-fetches candidates, de-duplicates by content hash, and re-ranks by
`score * language authority` (code and SQL definitions outrank prose) before building citations and
asking the text-generation client to answer strictly from the retained evidence.

LM Studio and Qdrant are optional at development time. Failed calls retry a configurable number of
times before falling back to deterministic local embeddings and an in-memory vector index. The
in-memory index is process-local and should not be used as a production persistence layer.

