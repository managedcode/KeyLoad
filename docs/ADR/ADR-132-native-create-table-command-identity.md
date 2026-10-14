# ADR-132: Native CREATE TABLE and caller-stable SQL command identity

- Status: Accepted implementation contract; compilation and qualification pending.
- Decision date: 2026-10-14.
- Owning criteria: REQ-SQLC-006 / AC-SQLC-006, REQ-REL-001/003, AC-TSQL-003/006.
- Feature: [Bounded CREATE TABLE](../Features/RelationalStorage/CreateTable.md).
- Related decisions: [ADR-130](ADR-130-microsoft-scriptdom-tsql.md) and [ADR-125](ADR-125-connection-owned-execution.md).

## Context and scope

SQL is KeyLoad's common language for authorized bounded database operations. The selected ScriptDOM 180.140.0 admission and existing native connection execution remain authoritative. A declarative CREATE needs an original caller-owned mutation identity and atomic create-only behavior; an existence preflight followed by ConfigureResource would not provide that behavior. No server-generated Guid, Configure no-op reinterpretation, old-parser fallback, migration or new dispatcher is introduced.

This decision delivers one closed CREATE TABLE profile. It does not qualify full SQL, general SQL Server CONTEXT_INFO semantics, PostgreSQL SQL compatibility, transactions spanning independent calls, power-loss durability or Linux/RF3 acceptance.

## Declarative schema contract

One permanent, schema-unqualified CREATE TABLE lowers to one canonical ResourceDefinition with Kind Collection, Authority Document and the caller's actual transaction domain. NVARCHAR(MAX), BIT and BIGINT lower respectively to the existing Text, Boolean and WholeNumber validators. Exactly one nonnullable Text inline primary key is required. Other columns explicitly specify NULL or NOT NULL. Duplicate columns, missing primary key and an explicitly nullable primary key refuse as Validation; selected but unsupported type/storage/constraint shapes refuse as UnsupportedCapability. Existing canonical schema, identifier, payload, depth, token, metadata and row validators remain in force.

ALTER, DROP, temporary names, qualified schema names, CTAS/CLONE, graph/file forms, table or named constraints, secondary unique/FK/check/default/computed/generated/identity/collation and storage options are outside this profile. The shared native parser and checked token stream retain all original budget callbacks and cancellation. CREATE and the exact connection control are shape-validated before prepared statement publication; zero positional inputs are required. There is no fake partition during preparation. Execution uses the actual admitted partition and revalidates canonical resource/schema authority.

## Original mutation identity

Public SDK/MCP SqlOperationRequest appends optional typed Guid CommandId at stable Orleans field Id 7. Existing fields 0 through 6, alias and AST/dialect versions remain unchanged. Reads need no command identity and refuse a declared mutation identity. CREATE requires a nonempty original Guid. Existing EXEC typed envelopes retain their own original Guid; a separately declared Guid must agree. Empty, missing required and conflicting identities refuse before dispatch. Neither retry nor cold continuation manufactures a replacement identity.

The native session uses one restricted caller-controlled control: SET CONTEXT_INFO 0x followed by exactly 32 hexadecimal UUID characters. The bytes name the UUID in RFC4122 display-order/N format, not .NET Guid.ToByteArray ordering. Guid.Empty is invalid. This is an explicitly versioned KeyLoad identity bridge, not a claim of general SQL Server context interoperability or native driver support beyond the real tested profile.

A successful control arms exactly one Guid on the real connection catalog. The catalog charges 16 logical bytes through both existing per-session and global retained admission before publishing the arm. It stores no trusted role, credential, request payload, prior history or authoritative outcome. A second control while armed refuses and releases the first arm. The next data operation atomically takes and releases the arm before compile/dispatch. An armed read or mismatched EXEC refuses. Normal successful Sync preserves an unused arm so real extended-protocol commands can be composed. Error-recovery Sync, execution/delivery failure, cancellation, data-operation settlement and disconnect clear/release it. No control-side database effect or fake HttpContext is used.

Fresh persisted credential identity is verified at operation admission. Every dispatched command still uses current persisted permission and policy-epoch checks, the server-owned Orleans connection grain with separately signed operation GUIDs, original CQRS streaming/cancellation and node-local ownership. A control does not bypass those checks. Producer settlement, reply delivery/backpressure, cancellation end, retained-reply disposal and connection/catalog disposal remain joined; original primary and cleanup failures share the ordered failure ledger.

## Canonical command and atomicity

CreateTable appends OperationKind 51 without renumbering existing operations. CreateTableRequest retains the canonical typed TenantId, DatabaseId and ResourceDefinition in stable native Orleans fields 0, 1 and 2 under KeyLoad.Relational.CreateTableRequest.v1. It routes through the same CatalogPartition as resource metadata, native payload normalization/fingerprint, global original-outcome key and persisted ClusterAdministrator authority.

The node-local apply transaction validates the canonical definition and checks the resource key inside the existing IAtomicTransaction/apply gate. An absent resource is written once. Any existing resource for a different original command refuses as Conflict even when its definition is equal. The same original command follows the normal successful outcome replay path. Changing its payload retains the existing fingerprint/conflict contract. Concurrent different original Guids therefore have exactly one successful CREATE and one normal failed Conflict outcome.

A first admitted domain failure can persist its normal failed outcome and commit metadata. Tests must not assert an unchanged whole raw cut against the state before that first failure. Resource rows and outbox effects remain absent; subsequent replay is compared against the captured full post-failure state/cut. Existing stored outcomes remain byte exact when later policy epochs fence their delivery with PermissionDenied. Restoring permissions uses a fresh current Guid for new work; it does not turn old epoch outcomes into current successful operations.

```mermaid
sequenceDiagram
    participant Caller
    participant Connection
    participant ConnectionGrain
    participant CatalogHost
    Caller->>Connection: SQL + original CommandId (or one-shot native arm)
    Connection->>Connection: Fresh identity, bounded native syntax and identity validation
    Connection->>ConnectionGrain: Canonical CreateTable 51
    ConnectionGrain->>CatalogHost: Fresh persisted administrator and epoch checks
    CatalogHost->>CatalogHost: Atomic original-outcome selection and resource absence check
    alt Same original command
        CatalogHost-->>Caller: Original complete outcome, no reapplication
    else Absent resource
        CatalogHost->>CatalogHost: Canonical resource + original outcome through RF3 barriers
        CatalogHost-->>Caller: Complete ResourceDefinition
    else Different original command, existing resource
        CatalogHost-->>Caller: Conflict; original failed outcome retained
    end
    Connection->>Connection: Join producer/delivery and release command identity
```

## Whole-operation evidence and remaining gates

SqlCreateTableWholeFlowTests.CallerStableCreateOnlySchemaConstraintRepairAndOriginalOutcomeReplayAcrossTwoColdOwners covers genuine native compile/apply, malformed and unsupported schema refusal, exact-token cancellation, schema/row repair, independent full SQL and AST rows, original successful/failed native outcome bytes, policy epochs, full raw state/cut conservation after replay, and two actual ZoneTree cold owners with fresh healthy writes.

SqlCreateTableRf3WholeFlowTests.OriginalCallerIdentityCreatesOnlyOnceAcrossSdkOfficialMcpNativeSessionAndTwoColdOwners covers real SDK, official MCP and Npgsql sessions on the fixture-owned Aspire Docker RF3 topology: same original success/failure replay, a genuine two-Guid CREATE race, one-shot arm misuse, current persisted permission denial/restoration, authentic original-producer AuthorizationReload hold/cancellation/ProducerDisposed settlement, full literal rows, separate minimum-token receipt reads and two owned three-node cold waves. Public conservation assertions cover their declared complete document/outbox image. They do not independently prove RF3 physical raw-image conservation or physical query-owner/cut correlation.

The current prepared source union has actual guarded compound native applied=N plus exact NotFound/creation NotSupported receipts for 37 proposed roles and a finite source-only peer closure. Those receipts prove preimage admission and creation fallback eligibility, not semantic compilation. Root source integration, whole formatter/build, original meaningful TUnit operations, process recovery and real Linux Aspire RF3 gates remain OPEN until their actual retained executions complete. No current task, protocol, full-SQL or durability qualification is inferred from this ADR.

## Accepted VALUES ownership-fence extension

The owner-approved ordinary INSERT extension is specified in [Ordinary typed SQL INSERT](../Features/RelationalStorage/OrdinarySqlInsert.md). SqlOperationRequest appends nullable positive OwnershipEpoch at stable field Id 8; Ids 0–7, aliases and existing defaults remain unchanged. Omission retains initial epoch 1. Only ordinary VALUES lowers the caller's explicit fence unchanged into the existing canonical Batch CommandRequest. No server-generated epoch, old-Guid rebinding, administrative privilege for row insertion, retry heuristic or new routing/dispatcher is introduced. CREATE and EXEC retain their original envelope semantics and refuse this separate outer fence.

For this closed extension, SET CONTEXT_INFO accepts its original 16-byte Guid or 24 bytes containing that RFC4122 display-order Guid followed by the exact positive Int64 epoch encoded as 16 hexadecimal big-endian/display-order characters. Catalog admission charges the complete 16/24 bytes before storing either value; it takes and clears both together before target compilation. Replacement, error Sync, settlement, cancellation and disconnect release both, while normal successful Sync preserves an unused arm. The current persisted subject, permissions, principal epoch, signed command and physical apply-gate fence remain authoritative. A 24-byte arm is not permission or prior history, and is not a general SQL Server context contract.

Actual moved-owner native and public whole-operation drafts distinguish caller fencing from discovery: the fixture's administrator captures genuine published placement solely for independent receipt assertions; the writer remains a persisted nonadministrator and explicitly supplies epoch 2. Automatic authorized nonadministrator epoch discovery/routing, full protocol interoperability, Linux RF3, cancellation/resource and durability qualification remain OPEN until actual joined evidence. Existing CREATE-specific 16-byte controls and original qualification history remain intact.
