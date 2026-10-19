# Bounded declarative CREATE TABLE

## Create-only identity

Implementation contract: [ADR-132](../../ADR/ADR-132-create-table-command-identity.md), with selected syntax under [ADR-130](../../ADR/ADR-130-microsoft-scriptdom-tsql.md).

REQ-SQLC-006 / AC-SQLC-006, REQ-REL-001/003 and AC-TSQL-003/006 define the bounded first declarative table family under the selected ScriptDOM grammar. One schema-unqualified permanent CREATE TABLE lowers into one CreateTableRequest through the existing signed connection execution, fresh persisted ClusterAdministrator authority, CatalogPartition cluster and node-local transaction/apply gate. It supports NVARCHAR(MAX), BIT and BIGINT, exactly one nonnullable Text inline primary key, and explicit NULL/NOT NULL on other columns. Temporary names, CREATE AS/CLONE/graph/file/storage clauses, named/table constraints, secondary uniqueness, defaults, computed/generated/identity columns, collation, foreign keys, checks, other scalar types, ALTER and DROP refuse the existing UnsupportedCapability or defined malformed Validation categories. Canonical schema/identifier/resource bounds remain authoritative; no schema or storage migration is provided.

Public SqlOperationRequest appends optional typed CommandId at generated field7; fields0–6 and its existing alias/version stay stable. Reads carry no identity. CREATE requires a nonempty original caller Guid. Existing EXEC typed envelopes retain their original Guid, and a supplied SQL identity must agree; no server Guid is manufactured for a mutation. Different original commands cannot overwrite an existing resource, even with identical metadata. Same original input replays its saved result; changed input conflicts. Existence is checked inside the transaction after cached-outcome/current-policy admission, not by a preliminary resource read. A first admitted duplicate failure persists its ordinary failed outcome/commit metadata; only resource/row/outbox effects are absent. Replay compares the full captured failed outcome and post-failure image/cut without additional changes.

The restricted connection bridge is SET CONTEXT_INFO with one exact nonempty sixteen-byte RFC4122 hexadecimal Guid literal. Prepare and execution use the same bounded AST shape. It returns this profile's one JSONB true control result and carries no data write. Successful ordinary Sync preserves the arm for the next Execute; the next data admission atomically takes/releases the sixteen-byte grant, with reads and mismatched EXEC identities refusing. Replacement, failed setter/prepare/error-recovery Sync, failed delivery, cancellation and disconnect clear it through the original producer/End/registry/catalog cleanup ledger. State is charged through existing per-session and global Catalog admission. This is not general SQL Server session-context or wire compatibility.

Owning complete operation cases are SqlCreateTableWholeFlowTests (real ZoneTree) and SqlCreateTableClusterWholeFlowTests (genuine Aspire cluster, SDK, official MCP and Npgsql). They retain literal complete schema/rows, malformed/unsupported refusals→same unadmitted Guid repair, atomic create-only/original success+failure replay, current policy denial/repair and old outcome fences, real admitted original-command cancellation/ProducerDisposed, and healthy revision2/3 after both owned cold waves. Unit scopes capture original outcome bytes and exact whole-store/cut; public query cuts remain opaque and are never compared numerically with cluster receipt positions. General CREATE/ALTER/DROP/DDL, ordinary SQL row INSERT and full SQL/client compatibility remain OPEN. Source/N/peer/compilation are distinct from original operation runtime and delivered Linux qualification; no gate is closed by this draft.

```mermaid
flowchart LR
 A[Caller original Guid] --> B[ScriptDOM admission]
 B --> C[Fresh persisted admin and signed connection]
 C --> D[Original outcome or transactional absence]
 D --> E[Catalog cluster commit]
 E --> F[Complete result and joined delivery]
```
