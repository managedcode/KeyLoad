# Selected-text search and retained maintenance SQL callers

ADR-130 selects the Microsoft ScriptDOM ExecuteStatement path. The selected-generation search helper (`NativeTextSelectedClusterCall`) and the old-generation maintenance refusal assertions (`NativeTextMaintenanceClusterReplayAssertions`) use the T-SQL EXEC syntax so that the selected-index results and the HistoryUnavailable oracle are exercised.

- REQ-SEARCH-NATIVE-CALLERS-001: the selected-search helper sends `EXEC keyload_search_execute @arguments` with the original partition and exact arguments object through all four SDK/MCP/SDK-SQL/MCP-SQL adapters; the selected IndexGeneration, ranking/result literals, read cuts and fresh persisted authorization are preserved.
- REQ-SEARCH-NATIVE-CALLERS-002: the retained-generation replay assertions send `EXEC keyload_search_text_maintain @arguments`, preserving the original command IDs, all four adapter paths, the HistoryUnavailable and null/undefined response checks, the unchanged outbox state, canonical rows, current selected generation, warm replay, restore/no-op and real cold continuation.
- AC-SEARCH-NATIVE-CALLERS-001: the callers are reviewed against ScriptDomExecOperation, SqlOriginalMutationIdentity and the search/text catalog decoders; there is no legacy fallback, parser, compatibility path or weakened result.
- AC-SEARCH-NATIVE-CALLERS-002: the selected-generation and text-maintenance whole operations run on the Aspire replicated cluster with the real SDK and official MCP callers, including the four paths, all old/current/no-op checks and cold recovery. Native SQL syntax, storage semantics, protocol and performance are separate qualification levels.

This contract is independent of the online-maintenance caller of [OnlineTextNativeSqlCaller](OnlineTextNativeSqlCaller.md).

```mermaid
flowchart LR
    A[Selected search or replay call] --> B[T-SQL EXEC through SDK and MCP]
    B --> C[Same native operation as the direct route]
```
