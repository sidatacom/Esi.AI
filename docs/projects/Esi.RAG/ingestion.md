# Ingestion

Chunk ids are deterministic (derived from repository + commit + path + line range), so re-indexing unchanged content upserts the same points instead of duplicating them.

## Tenant data synchronization

Tenant synchronization is separate from repository indexing. `TenantIngestionService` resolves the authenticated tenant, loads tenant configuration, invokes the explicit SQL or shared Graph source port, embeds `TenantDataItem` content, and writes `TenantDataDocument` values to the tenant collection. Every document carries tenant/source identity, source path, modification timestamp, content hash, and ACL metadata. Source adapters return deletion paths and resumable cursors/change tokens; synchronization state is stored per tenant/source pair. It is not valid to use this flow to discover arbitrary SQL tables or to write tenant content to the code collection.

The host application must register the MultiTenant `EsiDbContext` resolver and SQL/Graph connector implementations. SharePoint and OneDrive intentionally share `ITenantGraphSource`. See [`tenant-data.md`](tenant-data.md).
