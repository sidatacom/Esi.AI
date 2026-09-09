# Using Esi.RAG

This guide covers the local setup for Esi.RAG, starting Qdrant, configuring the embedding service, indexing a repository, and querying the index from PowerShell.

## Prerequisites

- .NET 10 SDK
- Docker Desktop with Docker Compose
- LM Studio running an OpenAI-compatible server on the configured URL
- The embedding model configured in `LmStudio:EmbeddingModel` loaded in LM Studio

Run the commands below from the repository root (`D:\Git\Esi.RAG`):

```powershell
Set-Location D:\Git\Esi.RAG
```

## 1. Start Qdrant

Start the persistent Qdrant container and expose its HTTP API on port 6333:

```powershell
docker compose up -d qdrant
docker compose ps qdrant
Invoke-RestMethod http://localhost:6333/readyz
```

The Compose file stores Qdrant data in the named `qdrant_storage` volume. Stop the container without deleting its data with:

```powershell
docker compose stop qdrant
```

To stop and remove the container while retaining the named volume, use `docker compose down`. Do not use `docker compose down -v` unless the indexed collection should be deleted as well.

## 2. Configure the services

The CLI reads configuration from `src\Esi.RAG.Cli\appsettings.json`. The API reads its equivalent file at `src\Esi.RAG.Api\appsettings.json`. Environment variables override JSON values; nested settings use double underscores in PowerShell.

At minimum, configure these values:

```json
{
  "Rag": {
    "RepositoryPath": "D:\\Git\\YourRepository",
    "MaxFileSizeBytes": 2000000,
    "ChunkMaxLines": 80,
    "ChunkOverlapLines": 12,
    "EmbeddingConcurrency": 2,
    "UpsertBatchSize": 256
  },
  "LmStudio": {
    "BaseUrl": "http://localhost:1234",
    "ApiKey": "",
    "EmbeddingModel": "text-embedding-qwen3-embedding-4b",
    "ChatModel": "local-model",
    "TimeoutSeconds": 30,
    "RetryCount": 2,
    "RetryDelayMilliseconds": 250
  },
  "TenantRag": {
    "CollectionPrefix": "data-"
  },
  "Qdrant": {
    "BaseUrl": "http://localhost:6333",
    "CollectionName": "esi-rag",
    "ApiKey": "",
    "UseInMemoryFallback": false,
    "TimeoutSeconds": 30,
    "RetryCount": 2,
    "RetryDelayMilliseconds": 250
  }
}
```

`UseInMemoryFallback` should be `false` for normal use so that indexing and searches use persistent Qdrant storage. If it is `true`, the application can fall back to a process-local in-memory index when Qdrant is unavailable; that index is lost when the process exits.

### Configuration reference

| Section | Setting | Purpose |
| --- | --- | --- |
| `Rag` | `RepositoryPath` | Default repository used when no path is supplied to `index`. |
| `Rag` | `ExcludedDirectories` | Directory names skipped during recursive discovery. |
| `Rag` | `IncludedExtensions` | File extensions eligible for indexing. |
| `Rag` | `MaxFileSizeBytes` | Files larger than this are skipped. |
| `Rag` | `GeneratedPathPatterns`, `GeneratedContentMarkers` | Exclude generated files. |
| `Rag` | `SecretFileNamePatterns`, `SecretContentPatterns` | Exclude likely secret files or files containing detected credentials. |
| `Rag` | `ChunkMaxLines`, `ChunkOverlapLines` | Chunk size and overlap for indexed content. |
| `Rag` | `EmbeddingConcurrency`, `UpsertBatchSize` | Embedding parallelism and Qdrant upload batch size. |
| `LmStudio` | `BaseUrl`, `ApiKey` | LM Studio OpenAI-compatible endpoint and optional bearer key. |
| `LmStudio` | `EmbeddingModel`, `ChatModel` | Models used for embeddings and answer generation. |
| `LmStudio` | `TimeoutSeconds`, `RetryCount`, `RetryDelayMilliseconds` | Request timeout and retry policy. |
| `Qdrant` | `BaseUrl`, `ApiKey` | Qdrant endpoint and optional API key. |
| `Qdrant` | `CollectionName` | Collection in which chunks are stored. |
| `Qdrant` | `UseInMemoryFallback` | Whether unavailable Qdrant may be replaced by a non-persistent index. |
| `Qdrant` | `TimeoutSeconds`, `RetryCount`, `RetryDelayMilliseconds` | Qdrant request timeout and retry policy. |
| `TenantRag` | `CollectionPrefix` | Prefix for isolated tenant collections, default `data-`. |

For local overrides, environment variables avoid putting credentials in JSON files:

```powershell
$env:LmStudio__BaseUrl = "http://localhost:1234"
$env:LmStudio__ApiKey = "your-lm-studio-key"
$env:LmStudio__EmbeddingModel = "text-embedding-qwen3-embedding-4b"
$env:Qdrant__BaseUrl = "http://localhost:6333"
$env:Qdrant__CollectionName = "esi-rag"
$env:Qdrant__UseInMemoryFallback = "false"
$env:TenantRag__CollectionPrefix = "data-"
```

These variables apply only to the current PowerShell session. Remove them when needed with `Remove-Item Env:LmStudio__ApiKey` and the corresponding variable name.

## 3. Check dependencies

Use the CLI health command before indexing:

```powershell
& .\scripts\health.ps1
```

The result reports overall health and separate Qdrant and LM Studio status. Qdrant should be reachable and LM Studio should expose the configured embedding model.

You can also run the command directly:

```powershell
dotnet run --project .\src\Esi.RAG.Cli\Esi.RAG.Cli.csproj -- health
```

## 4. Index a repository

Index a repository by passing its path to the CLI:

```powershell
dotnet run --project .\src\Esi.RAG.Cli\Esi.RAG.Cli.csproj -- index D:\Git\YourRepository
```

The repository is discovered recursively. Files are filtered by extension, excluded directories, size, generated-content rules, and secret detection. Content is extracted, chunked, embedded through LM Studio, and uploaded to the configured Qdrant collection. Re-indexing is safe: deterministic chunk IDs cause unchanged chunks to be upserted rather than duplicated.

The equivalent PowerShell helper is:

```powershell
& .\scripts\index.ps1 -RepositoryPath "D:\Git\YourRepository"
```

The helper defaults to `D:\Git\Esi.RAG` when no path is supplied:

```powershell
& .\scripts\index.ps1
```

The legacy root helper can also be used:

```powershell
& .\ingest-default.ps1 -RepositoryPath "D:\Git\YourRepository"
```

The command writes progress to the console and prints an `IngestionReport` as JSON, including discovered, indexed, skipped, failed, embedded, and uploaded counts. Review skipped-file reasons and errors in that report.

## 5. Search and ask questions

Search returns matching citations from the indexed repository:

```powershell
& .\scripts\search.ps1 -Query "Where is Qdrant configured?"

dotnet run --project .\src\Esi.RAG.Cli\Esi.RAG.Cli.csproj -- search "Where is Qdrant configured?" --limit 5
```

Use filters when needed:

```powershell
dotnet run --project .\src\Esi.RAG.Cli\Esi.RAG.Cli.csproj -- search "authentication flow" --limit 10 --language csharp --path-prefix src
```

Ask runs a bounded, evidence-only investigation over the indexed content:

```powershell
& .\scripts\ask.ps1 -Query "How does ingestion upload chunks to Qdrant?"

dotnet run --project .\src\Esi.RAG.Cli\Esi.RAG.Cli.csproj -- ask "How does ingestion upload chunks to Qdrant?" --max-rounds 3 --max-citations 12
```

## 6. Run the API instead of the CLI

Start the API when using HTTP clients or the OpenAPI document:

```powershell
dotnet run --project .\src\Esi.RAG.Api\Esi.RAG.Api.csproj
```

Available endpoints are:

- `GET /health`
- `GET /health/qdrant`
- `GET /health/lmstudio`
- `POST /api/ingestion/index` with `{ "repositoryPath": "D:\\Git\\YourRepository" }`
- `POST /api/search` with `{ "query": "...", "limit": 5 }`
- `POST /api/ask` with `{ "query": "...", "maxRounds": 3, "maxCitations": 12 }`
- `POST /api/tenant/sync` with no tenant ID; the authenticated tenant context determines the source configuration and destination collection
- `POST /api/tenant/search` with `{ "query": "...", "limit": 5 }`; retrieval is restricted to the authenticated tenant and its ACLs

Tenant synchronization requires the host application's MultiTenant `EsiDbContext` resolver and the SQL/Graph connector registrations described in [`tenant-data.md`](tenant-data.md). The repository currently exposes explicit fail-loud adapters until those host implementations are registered.

The API and CLI use the same configuration sections, but each executable loads its own `appsettings.json`.

## Troubleshooting

- **Qdrant is unhealthy:** check `docker compose ps`, inspect logs with `docker compose logs qdrant`, and confirm `Qdrant:BaseUrl` is `http://localhost:6333`.
- **LM Studio is unhealthy:** start its local server, load the configured embedding model, and confirm `LmStudio:BaseUrl` and `LmStudio:EmbeddingModel`.
- **Indexing uses memory instead of Qdrant:** set `Qdrant:UseInMemoryFallback` to `false` and verify the health command before indexing.
- **No files are indexed:** check `IncludedExtensions`, `ExcludedDirectories`, file-size limits, and the skip reasons in the ingestion report.
