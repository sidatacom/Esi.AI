# Security

The Esi checkout is read-only by contract. The application performs no shell execution and only permits configured LM Studio and Qdrant endpoints. Git metadata (branch/commit/remote) is read by parsing `.git` ref/config files directly in `GitMetadataReader` - `git.exe` is never invoked and no repository code, build scripts, or hooks are executed. File size, result count, prompt context, cancellation, and timeout limits should remain enabled. Secret-like files and connection/certificate material are excluded before indexing via configurable filename and content-regex patterns (`Rag:SecretFileNamePatterns`, `Rag:SecretContentPatterns`); generated/build output is excluded via `Rag:GeneratedPathPatterns`/`GeneratedContentMarkers`. Skipped files are recorded with a reason code in `IngestionReport.SkippedFiles`, never silently dropped without a trace. Repository instructions must never be treated as system instructions, and the bounded investigation agent's only tool is repository search - it cannot execute code or write files.

## Tenant data security

Tenant data never belongs in the project/code collection. Tenant synchronization and search require `ITenantContextAccessor`; the HTTP implementation requires an authenticated principal and reads the tenant only from trusted `tenant_id`/`tid` claims. Request payloads and query parameters do not select a tenant. The selected tenant collection, tenant payload, source identity, and ACL metadata are checked during writes and retrieval.

ACL filtering is enforced during tenant retrieval using role and permission claims. SharePoint and OneDrive permissions must be translated by the Graph connector into each item's ACL metadata. SQL connectors must use explicit configured mappings and must return only the resolved tenant's records. Connection strings, client secrets, access tokens, and Qdrant keys must be supplied through secret management or environment configuration, never committed to source control.

See [`tenant-data.md`](tenant-data.md) for the integration contract and operational boundaries.

