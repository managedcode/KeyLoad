# Online text maintenance through the SQL caller

ADR-130 selects Microsoft ScriptDOM T-SQL. The online text maintenance operation is called as `EXEC keyload_search_text_online_maintain @arguments`, which the SqlOperationCompiler admits as an ExecuteStatement through ScriptDomExecOperation with the procedure argument. SDK and official MCP SQL callers must use this statement.

- REQ-ONLINE-SQL-CALLER-001: the SDK SQL and official MCP SQL callers send the EXEC statement with the original parameter-envelope object. Operation and command identities, partition, SDK and official MCP adapters, error assertions, replay, mutation and cold-open checks are preserved; there is no CALL fallback and no extra parser.
- AC-ONLINE-SQL-CALLER-002: the required Aspire-owned replicated-cluster qualification covers Sdk, Mcp, SdkSql and McpSql. Every route must retain all four preflight owner refusals, the fresh complete online publication, current rows, the update/delete continuation, original full replay and cold recovery. This complete caller qualification remains open; the current local operation and process-recovery cases are listed in [OnlineGenerationLifetime](OnlineGenerationLifetime.md#evidence).

This caller contract does not qualify SQL conformance, publication, recovery or performance by itself.

```mermaid
flowchart LR
    A[Online text maintenance call] --> B[T-SQL EXEC through SDK and MCP]
    B --> C[Same operation as the direct route]
```
