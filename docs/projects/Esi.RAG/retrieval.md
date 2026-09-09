# Retrieval

Search embeds the query and uses vector similarity, preserving source path and line ranges as citations. Metadata filters (`SearchFilter.Languages`, `PathPrefixes`, `SymbolPrefixes`) are applied both at the Qdrant payload level and client-side, so schema investigations can explicitly restrict retrieval to C# or a repository path without repository-specific rules. Results are over-fetched (up to eight times the requested citation limit), deduplicated by content hash, and re-ranked by language authority plus exact identifier-term matches. This favors evidence containing terms such as `HasColumnName`, `TenantMapInt`, `MndName`, or `Property(e => e.Name)` while still honoring the requested return limit.

C# extraction emits additional `ef-mapping:*` segments for `Property(...).HasColumnName(...)` expressions. These segments retain the original source line range and complete mapping statement, making EF Core schema evidence directly searchable and citable.
