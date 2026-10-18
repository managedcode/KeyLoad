# Online text maintenance through the native SQL caller

ADR-130 selects Microsoft ScriptDOM T-SQL. The online text maintenance operation is called as `EXEC keyload_search_text_online_maintain @arguments`, which the SqlOperationCompiler admits as an ExecuteStatement through ScriptDomExecOperation with the native procedure argument. `NativeTextOnlineClusterFlow` runs all four owner-refusal caller paths before the selected fresh operation, so the SQL caller must use this statement.

- REQ-ONLINE-SQL-CALLER-001: the SDK SQL and official MCP SQL callers send the native EXEC statement with the original parameter-envelope object. Operation and command identities, partition, SDK and official MCP adapters, error assertions, replay, mutation and cold-open checks are preserved; there is no CALL fallback and no extra parser.
- AC-ONLINE-SQL-CALLER-002: `NativeTextOnlineClusterTests.OnlinePublicationRefusalUpdateDeleteFullReplayAndColdContinuation` runs for Sdk, Mcp, SdkSql and McpSql on its Aspire-owned replicated-cluster fixture. Every case retains all four preflight owner refusals, the fresh complete online publication, current rows, the update/delete continuation, original full replay and cold recovery.

This caller contract does not qualify SQL conformance, publication, recovery or performance by itself.

```mermaid
flowchart LR
    A[Online text maintenance call] --> B[T-SQL EXEC through SDK and MCP]
    B --> C[Same native operation as the direct route]
```
