# ADR-132: CREATE TABLE and caller-stable SQL command identity

- Status: Accepted implementation contract; compilation and qualification pending.
- Decision date: 2026-10-14.
- Owning criteria: REQ-SQLC-006 / AC-SQLC-006, REQ-REL-001/003, AC-TSQL-003/006.
- Feature: [Bounded CREATE TABLE](../Features/RelationalStorage/CreateTable.md).
- Related decisions: [ADR-130](ADR-130-microsoft-scriptdom-tsql.md) and [ADR-125](ADR-125-connection-owned-execution.md).

## Context and scope

SQL is KeyLoad's common language for authorized bounded database operations. The selected ScriptDOM 180.140.0 admission and existing connection execution remain authoritative. A declarative CREATE needs an original caller-owned mutation identity and atomic create-only behavior; an existence preflight followed by ConfigureResource would not provide that behavior. No server-generated Guid, Configure no-op reinterpretation, old-parser fallback, migration or new dispatcher is introduced.

This decision delivers one closed CREATE TABLE profile. It does not qualify full SQL, general SQL Server CONTEXT_INFO semantics, PostgreSQL SQL compatibility, transactions spanning independent calls, power-loss durability or Linux/cluster acceptance.

## Declarative schema contract

One permanent, schema-unqualified CREATE TABLE lowers to one canonical ResourceDefinition with Kind Collection, Authority Document and the caller's actual transaction domain. NVARCHAR(MAX), BIT and BIGINT lower respectively to the existing Text, Boolean and WholeNumber validators. Exactly one nonnullable Text inline primary key is required. Other columns explicitly specify NULL or NOT NULL. Duplicate columns, missing primary key and an explicitly nullable primary key refuse as Validation; selected but unsupported type/storage/constraint shapes refuse as UnsupportedCapability. Existing canonical schema, identifier, payload, depth, token, metadata and row validators remain in force.

ALTER, DROP, temporary names, qualified schema names, CTAS/CLONE, graph/file forms, table or named constraints, secondary unique/FK/check/default/computed/generated/identity/collation and storage options are outside this profile. The shared parser and checked token stream retain all original budget callbacks and cancellation. CREATE and the exact connection control are shape-validated before prepared statement publication; zero positional inputs are required. There is no fake partition during preparation. Execution uses the actual admitted partition and revalidates canonical resource/schema authority.

## Original mutation identity

Public SDK/MCP SqlOperationRequest appends optional typed Guid CommandId at stable Orleans field Id 7. Existing fields 0 through 6, alias and AST/dialect versions remain unchanged. Reads need no command identity and refuse a declared mutation identity. CREATE requires a nonempty original Guid. Existing EXEC typed envelopes retain their own original Guid; a separately declared Guid must agree. Empty, missing required and conflicting identities refuse before dispatch. Neither retry nor cold continuation manufactures a replacement identity.

The session uses one restricted caller-controlled control: SET CONTEXT_INFO 0x followed by exactly 32 hexadecimal UUID characters. The bytes name the UUID in RFC4122 display-order/N format, not .NET Guid.ToByteArray ordering. Guid.Empty is invalid. This is an explicitly versioned KeyLoad identity bridge, not a claim of general SQL Server context interoperability or driver support beyond the real tested profile.

A successful control arms exactly one Guid on the real connection catalog. The catalog charges 16 logical bytes through both existing per-session and global retained admission before publishing the arm. It stores no trusted role, credential, request payload, prior history or authoritative outcome. A second control while armed refuses and releases the first arm. The next data operation atomically takes and releases the arm before compile/dispatch. An armed read or mismatched EXEC refuses. Normal successful Sync preserves an unused arm so real extended-protocol commands can be composed. Error-recovery Sync, execution/delivery failure, cancellation, data-operation settlement and disconnect clear/release it. No control-side database effect or fake HttpContext is used.

Fresh persisted credential identity is verified at operation admission. Every dispatched command still uses current persisted permission and policy-epoch checks, the server-owned Orleans connection grain with separately signed operation GUIDs, original CQRS streaming/cancellation and node-local ownership. A control does not bypass those checks. Producer settlement, reply delivery/backpressure, cancellation end, retained-reply disposal and connection/catalog disposal remain joined; original primary and cleanup failures share the ordered failure ledger.

## Canonical command and atomicity

CreateTable appends OperationKind 51 without renumbering existing operations. CreateTableRequest retains the canonical typed TenantId, DatabaseId and ResourceDefinition in stable Orleans fields 0, 1 and 2 under KeyLoad.Relational.CreateTableRequest. It routes through the same CatalogPartition as resource metadata, payload normalization/fingerprint, global original-outcome key and persisted ClusterAdministrator authority.

The node-local apply transaction validates the canonical definition and checks the resource key inside the existing IAtomicTransaction/apply gate. An absent resource is written once. Any existing resource for a different original command refuses as Conflict even when its definition is equal. The same original command follows the normal successful outcome replay path. Changing its payload retains the existing fingerprint/conflict contract. Concurrent different original Guids therefore have exactly one successful CREATE and one normal failed Conflict outcome.

A first admitted domain failure can persist its normal failed outcome and commit metadata. Tests must not assert an unchanged whole raw cut against the state before that first failure. Resource rows and outbox effects remain absent; subsequent replay is compared against the captured full post-failure state/cut. Existing stored outcomes remain byte exact when later policy epochs fence their delivery with PermissionDenied. Restoring permissions uses a fresh current Guid for new work; it does not turn old epoch outcomes into current successful operations.

```mermaid
sequenceDiagram
    participant Caller
    participant Connection
    participant ConnectionGrain
    participant CatalogHost
    Caller->>Connection: SQL + original CommandId (or one-shot arm)
    Connection->>Connection: Fresh identity, bounded syntax and identity validation
    Connection->>ConnectionGrain: Canonical CreateTable 51
    ConnectionGrain->>CatalogHost: Fresh persisted administrator and epoch checks
    CatalogHost->>CatalogHost: Atomic original-outcome selection and resource absence check
    alt Same original command
        CatalogHost-->>Caller: Original complete outcome, no reapplication
    else Absent resource
        CatalogHost->>CatalogHost: Canonical resource + original outcome through required-copy barriers
        CatalogHost-->>Caller: Complete ResourceDefinition
    else Different original command, existing resource
        CatalogHost-->>Caller: Conflict; original failed outcome retained
    end
    Connection->>Connection: Join producer/delivery and release command identity
```

## Whole-operation evidence and remaining gates

SqlCreateTableWholeFlowTests.CallerStableCreateOnlySchemaConstraintRepairAndOriginalOutcomeReplayAcrossTwoColdOwners covers genuine compile/apply, malformed and unsupported schema refusal, exact-token cancellation, schema/row repair, independent full SQL and AST rows, original successful/failed outcome bytes, policy epochs, full raw state/cut conservation after replay, and two actual ZoneTree cold owners with fresh healthy writes.

SqlCreateTableClusterWholeFlowTests.OriginalCallerIdentityCreatesOnlyOnceAcrossSdkOfficialMcpSessionAndTwoColdOwners covers real SDK, official MCP and Npgsql sessions on the fixture-owned Aspire Docker cluster topology: same original success/failure replay, a genuine two-Guid CREATE race, one-shot arm misuse, current persisted permission denial/restoration, authentic original-producer AuthorizationReload hold/cancellation/ProducerDisposed settlement, full literal rows, separate minimum-token receipt reads and two owned three-node cold waves. Public conservation assertions cover their declared complete document/outbox image. They do not independently prove physical raw-image conservation across nodes.

Source integration, whole formatter/build, original meaningful TUnit operations, process recovery and real Linux Aspire cluster gates remain OPEN until their actual retained executions complete. No current task, protocol, full-SQL or durability qualification is inferred from this ADR.
