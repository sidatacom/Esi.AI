# Esi.RAG MVP

Practical .NET 10 RAG MVP with domain/application/infrastructure/ingestion/API/CLI projects.

## Included
- `Esi.RAG.sln`, `global.json`, central package management
- Rich domain model: `RepositoryDocument`, `ProjectMetadata`, `DocumentChunk`, `CodeSymbol`, `SqlObject`,
  `SearchQuery`/`SearchFilter`, `Citation`, `InvestigationPlan`/`Step`/`Result`, `AgentState`, `IngestionReport`
- Safe, execution-free Git metadata reader (parses `.git` ref/config files only - never shells out to git
  or executes anything from the target repository)
- Stable, reproducible chunk IDs derived from repository name + commit sha + relative path + line range
- Configurable exclusions: max file size, secret filename/content patterns, generated filename/content markers,
  requested extensions, excluded directories
- Roslyn-based C# extraction (namespace/containing-type qualified symbols) with syntax-fallback to whole file
- SQL object extraction (tables/views/procedures/functions) alongside statement-level segments
- Markdown heading-aware extraction
- Line-preserving chunker
- Real Qdrant HTTP adapter: collection ensure/create, payload upsert, payload-filtered search, delete-by-path,
  health probe - with automatic in-memory fallback
- Typed LM Studio HTTP client with retries, timeouts, and a health probe; deterministic embedding fallback
- Retrieval: over-fetch + de-duplication by content hash + authority ranking (code/SQL > markdown > prose)
- Bounded, evidence-only investigation agent (`repository_search` is its only tool - no code execution,
  no file writes) with a fixed round budget
- API: `/health`, `/health/qdrant`, `/health/lmstudio`, `/api/ingestion/index`, `/api/search`, `/api/ask`, `/api/tenant/sync`, `/api/tenant/search`
- CLI built with `System.CommandLine`: `health`, `index`, `search`, `ask`
- Unit/integration-style tests across all projects
- Tenant-aware data RAG with isolated SQL/Graph source ports, ACL-aware retrieval, resumable synchronization, and per-tenant Qdrant collections

## Quick start
```powershell
dotnet restore .\Esi.RAG.sln
dotnet build .\Esi.RAG.sln -c Release
dotnet test .\Esi.RAG.sln -c Release
dotnet run --project .\src\Esi.RAG.Api
dotnet run --project .\src\Esi.RAG.Cli -- health
dotnet run --project .\src\Esi.RAG.Cli -- index [repositoryPath]
dotnet run --project .\src\Esi.RAG.Cli -- search "query" --limit 5 --language csharp
dotnet run --project .\src\Esi.RAG.Cli -- ask "question" --max-rounds 3 --max-citations 12
```

## API
- `GET /health` - overall status snapshot
- `GET /health/qdrant` - live Qdrant reachability probe (503 if unhealthy)
- `GET /health/lmstudio` - live LM Studio reachability probe (503 if unhealthy)
- `POST /api/ingestion/index` - `{ repositoryPath }` -> `IngestionReport`
- `POST /api/search` - `{ query, limit?, languages?, pathPrefixes? }` -> `SearchResult`
- `POST /api/ask` - `{ query, maxRounds?, maxCitations?, languages?, pathPrefixes? }` -> `InvestigationResult`
- `POST /api/tenant/sync` - synchronizes the authenticated tenant; tenant ID comes from the authenticated context
- `POST /api/tenant/search` - `{ query, limit? }` -> searches only the authenticated tenant data collection

Tenant endpoints require host application registration of the MultiTenant configuration resolver and the SQL/Graph source connectors. See [`docs\\tenant-data.md`](docs/tenant-data.md).

## CLI
- `health`
- `index [repositoryPath]`
- `search <query> [--limit N] [--language ...] [--path-prefix ...]`
- `ask <query> [--max-rounds N] [--max-citations N] [--language ...] [--path-prefix ...]`

Scripts: `scripts\\health.ps1`, `scripts\\index.ps1`, `scripts\\search.ps1`, `scripts\\ask.ps1`, `scripts\\investigate.ps1`.

For a complete local setup, including starting Qdrant, configuring LM Studio, indexing, and PowerShell examples, see [`docs\\usage.md`](docs/usage.md).

## Configuration
Set `Rag__*`, `LmStudio__*`, `Qdrant__*`, and `TenantRag__*` via appsettings/environment. The default repository path is the current directory; override it for another checkout. Keep credentials, connection strings, and access tokens outside committed configuration.

Notable `Rag` options: `MaxFileSizeBytes`, `SecretFileNamePatterns`, `SecretContentPatterns`,
`GeneratedPathPatterns`, `GeneratedContentMarkers`, `ExcludedDirectories`, `IncludedExtensions`,
`ChunkMaxLines`, `ChunkOverlapLines`. Files matching secret/generated/size rules are excluded from
indexing and reported in `IngestionReport.SkippedFiles` with a reason code.

## Tenant and code index boundaries

The code index is project-scoped and is not duplicated per tenant. Tenant data is stored in isolated collections such as `data-tenant-001` and includes tenant/source identity, source path, modification time, content hash, and ACL metadata in every document. Retrieval requires an authenticated tenant context and applies ACL filtering. SharePoint and OneDrive use the same Microsoft Graph connector port; SQL synchronization uses explicit configured mappings rather than arbitrary table discovery. See [`docs\\tenant-data.md`](docs/tenant-data.md) for host integration and developer guidance.

## Limitations
- In-memory index is process-local and non-persistent (used only as a fallback/test double).
- Deterministic embeddings are fallback-only and lower quality than a real LM Studio embedding model.
- SQL extraction is statement/keyword based, not full T-SQL parsing.
- Markdown extraction is heading-based and does not interpret the full Markdown AST.
- Git metadata reading covers repository-level branch/commit/remote only; it does not compute
  per-file blame/last-commit history (that would require walking commit objects, which is out of
  scope for a safe, execution-free reader).
