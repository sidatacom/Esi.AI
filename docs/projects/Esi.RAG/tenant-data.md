# Tenant data RAG

Tenant retrieval is a separate concern from repository/code retrieval.

## Scope model

The project/code index uses the existing `Qdrant:CollectionName` (normally `esi-rag`). It is shared by the project and contains source code, EF models, mappings, migrations, relationships, constraints, and other schema knowledge.

Tenant data is stored separately in a collection named from `TenantRag:CollectionPrefix` and the authenticated tenant ID. The default is:

```text
code:         esi-rag
tenant 001:   data-tenant-001
tenant 002:   data-tenant-002
```

Tenant data is never written to the code collection. The tenant vector-store factory validates the tenant ID on writes and filters it again on reads.

## Runtime flow

1. `ITenantContextAccessor` obtains the tenant from the authenticated application context. The HTTP implementation accepts the `tenant_id` or `tid` claim only when the principal is authenticated.
2. `ITenantConfigurationResolver` resolves the tenant's configuration through the host application's MultiTenant `EsiDbContext` integration.
3. Explicit source mappings in `TenantConfiguration.Sources` select SQL, SharePoint, or OneDrive synchronization. Arbitrary SQL tables are not discovered automatically.
4. `ITenantSqlSource` reads the configured tenant database. `ITenantGraphSource` is the single connector port for both SharePoint and OneDrive.
5. `ITenantIngestionService` validates tenant and source identity, embeds content using the existing embedding client, stores ACL metadata, persists change tokens, and applies deletion deltas.
6. `ITenantSearchService` selects the collection for the authenticated tenant and applies ACL filtering before returning documents.

## Host integration

The repository does not contain the application-owned MultiTenant `EsiDbContext` or Microsoft Graph implementation. Register replacements for these explicit ports in the host application:

- `ITenantConfigurationResolver`
- `ITenantSqlSource`
- `ITenantGraphSource`
- `ITenantContextAccessor` when the host has a different authentication context

The default adapters fail explicitly when synchronization is requested; they do not return empty data or silently skip a source.

A source adapter should return a `TenantSourceBatch` containing:

- current `TenantDataItem` values;
- `DeletedSourcePaths` for records/items removed since the previous state;
- a Graph change token when available;
- a resumable cursor when the source uses paging.

Each item must carry `TenantId`, `SourceType`, `SourceId`, source path/item ID, modification time, content hash, and ACL role/permission sets.

## Configuration

```json
{
  "TenantRag": {
    "CollectionPrefix": "data-"
  }
}
```

Use environment variables for deployment-specific values:

```powershell
$env:TenantRag__CollectionPrefix = "data-"
$env:Qdrant__BaseUrl = "https://qdrant.example.internal"
$env:Qdrant__ApiKey = "set-through-secret-management"
```

Do not commit connection strings, client secrets, Graph tokens, or Qdrant keys. Tenant SQL and Graph settings belong in the existing application configuration model and must be resolved per tenant.

## API

The tenant endpoints do not accept a tenant ID in the request body:

- `POST /api/tenant/sync` synchronizes the tenant from the authenticated context.
- `POST /api/tenant/search` accepts `{ "query": "...", "limit": 5 }` and searches only the authenticated tenant collection.

The existing `/api/search`, `/api/ask`, and repository indexing endpoints remain code-scope operations and retain their previous behavior.

## Operational behavior

Synchronization is cancellable and reports `TenantSyncProgress`. Source state is stored independently for each tenant/source pair. Content hashes, modification timestamps, cursors, and change tokens allow adapters to return only changes. Deleted paths are removed from the tenant collection.

Qdrant failures use the existing configured fallback policy. The fallback is process-local and isolated per tenant; it is not a production persistence substitute. Set `Qdrant:UseInMemoryFallback` to `false` in production when persistent Qdrant availability is required.

## Testing guidance

Tenant implementations should test:

- separate collection selection and wrong-tenant access;
- authenticated-context enforcement;
- ACL role and permission filtering;
- explicit SQL mappings and incremental updates;
- SharePoint and OneDrive changes through the same Graph connector;
- deleted records/items;
- persisted cursor/change-token resume behavior;
- connector failures and cancellation.
