# Signed public query cursor request identity

Canonical slice: QueryExecution. Existing REQ-QUERY-003 and AC-MP-003/AC-MP-012, ADR-013/022/125 own this continuation.

## REQ-CURSOR-IDENTITY-001
A genuine unchanged-principal signed cursor must bind the full native AST request identity: partition, AST Query (Explain=false), parameters, AllowFullScan and AstVersion. Changing the supported OFFSET from 1 to 0 must refuse the original cursor as CursorExpired. This is a public request change, not a resigned or altered token.

## AC-CURSOR-IDENTITY-001
After the unchanged original AcSqlc006 whole flow, a genuine second SqlOffsetRf3Setup creates exactly four left and four right literal records at revision1 and the same original epoch2 Query|DocumentsRead identity. Both public clients capture real first OFFSET1 pages containing b, continue c then d at the same signed cut, and end with null cursor. The independent full four projected title rows and eight documents must match literal source values.

## AC-CURSOR-IDENTITY-002
For EACH SDK and official MCP changed-OFFSET refusal, assert the exact native CursorExpired Problem/safe detail and current full rows/documents, both resource metadata snapshots and full administrator outbox. Immediately retry the unchanged original cursor successfully and recheck all current state after each page. No scope, grant, schema, token bytes or deadline is changed.

## AC-CURSOR-IDENTITY-003
Replay the complete original eight-mutation seed command through EACH public client; compare every field of its original receipt and immediately verify full current state and fresh signed pages before another replay. No raw store/journal invariance or physical-cut/logical-commit inequality is claimed.

## AC-CURSOR-IDENTITY-004
Join all original/new caller owners before EACH of two genuine all-three Aspire RF3 cold cycles. Reopen fresh callers, reject old owner/read-generation cursors, verify fresh full literal pages and all original receipt replays. The unchanged original flow already owns two cold cycles: the strengthened complete Case therefore adds two, totaling four. Its original McpCallerDeadline and Test/Args identity are preserved; deadline-fit/compiler/Linux/runtime qualification remain OPEN.

```mermaid
flowchart LR
 O[Unchanged original Case flow] --> S[Native second eight-row seed]
 S --> C[Capture SDK and MCP signed b pages]
 C --> X[Changed OFFSET0 refuses]
 X --> V[Original OFFSET1 continues c then d]
 V --> R[Full seed receipt replay and current state]
 R --> K1[Join callers and all-three cold1]
 K1 --> F1[Old cursor refusal and fresh pages]
 F1 --> K2[Join callers and all-three cold2]
 K2 --> F2[Fresh pages and original receipts]
```

Backend/shared contracts: existing native QueryRequestIdentity/QueryCursorExecution/QueryEngine only; no product change. Frontend/benchmarks: N/A, public correctness qualification. Test: SqlOffsetRf3WholeFlowTests.AcSqlc006BoundedSdkOfficialMcpOffsetsContinueSignedCutsRepairPolicyAndReopenTwice, same noArgs UID unknown until actual census. Authored source preview is not source adoption, compilation or runtime qualification.
