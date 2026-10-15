# Online text maintenance through the native SQL caller

Status: corrective source contract; actual compiler and Aspire RF3 execution OPEN.

ADR-130 selects Microsoft ScriptDOM T-SQL. The existing NativeTextOnlineRf3Call helper still spells its operation as retired `CALL keyload_search_text_online_maintain(@arguments)`. The actual SqlOperationCompiler admits an ExecuteStatement through ScriptDomExecOperation, whose native procedure argument is `EXEC keyload_search_text_online_maintain @arguments`. NativeTextOnlineRf3Flow runs all four owner-refusal caller paths before the selected fresh operation, so this stale helper can block even a direct SDK or MCP scenario before its six real progress phases. This is a source-confirmed caller mismatch, not a claimed executed failure.

- REQ-ONLINE-SQL-CALLER-001: replace only that SQL string with the selected native EXEC statement. Preserve the original parameter-envelope object, operation and command identities, partition, SDK and official MCP adapters, error assertions, replay, mutation and cold-open checks. Add no CALL fallback or new parser.
- AC-ONLINE-SQL-CALLER-001: independently review the full single helper before/after against actual ScriptDomExecOperation and retain exact source guards. The only executable change is the SQL literal.
- AC-ONLINE-SQL-CALLER-002: execute the existing NativeTextOnlineRf3Tests.OnlinePublicationRefusalUpdateDeleteFullReplayAndColdContinuation for Sdk, Mcp, SdkSql and McpSql through its original test-owned Aspire RF3 fixture. Every case retains all four preflight owner refusals, the fresh complete online publication, current rows, update/delete continuation, original full replay and cold recovery. Do not skip SQL paths or reduce the test to the isolated string.

The native CQRS three-profile repair is a separate prerequisite for admitting the actual OnlineText progress. This caller correction does not qualify SQL conformance, publication, recovery or performance by itself.
