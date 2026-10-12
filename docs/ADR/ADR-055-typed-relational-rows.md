# ADR-055: Typed relational rows in canonical entity storage

Status: Accepted under owner direction2026-10-02; implementation/qualification pending.

Pre-delivery naming refinement: CA1720 rejected the new public enum members
Int64/Decimal in candidate6cfdadf38. Use WholeNumber (signed 64-bit integer) and
FixedPoint (exact .NET decimal range/scale). Preserve their ordinal values and
all scalar correctness contracts. This fixes the public identifiers rather than
suppressing the analyzer.

## Decision

Add optional null-omitted RelationalSchema metadata to Collection resources.
This is a typed table backed by the existing canonical document/entity keyspace,
not a second SQL storage engine or a new ResourceKind that breaks graph/vector
binding. Columns are immutable, unique and bounded. Initial scalar types: Text,
Boolean, Int64, Decimal and UtcTimestamp. A named nonnullable Text primary-key
column is mandatory and equals EntityRef.Id. Nullable columns accept absent/null;
other columns reject both. Rows are closed top-level JSON objects; unknown and
duplicate properties reject. Int64 and decimal must parse exactly into the stated
CLR scalar range; UTC timestamps carry explicit zero offset. Native configured
unique IndexDefinition constraints retain atomic-partition scope and SQL NULL
policy remains explicitly configured by IncludeNull/IncludeMissing. Q1 metadata
names cannot silently shadow row columns: `revision`, `*` and names starting `@`
are reserved; `id` is permitted only as the primary key matching EntityRef.Id.

Only Document-authority Collection resources can carry this schema. The same
PUT/PATCH final-image validation occurs before index updates in the existing
ordered atomic gate. Revision/authorization/replacement checks remain unchanged;
failed constraints roll back the complete mixed mutation batch. Canonical row
identities may be graph endpoints, vector subjects, event provenance and queued
workflow references. Blob lifecycle remains separate as ADR-038 defines.

PUT validates the original bounded JSON before numeric canonicalization. PATCH
preflights raw typed SET scalars, then validates the final image once; this rejects
decimal rounding/underflow before the original precision is lost. Both honor
configured document-byte/depth limits before parsing.

Foreign keys/checks/defaults/cascades and join planning need subsequent explicit
contracts and are not advertised by this typed-row stage. They remain
required relational product work, with partition/domain/policy/resource bounds.

```mermaid
flowchart LR
    Catalog[Persisted optional table schema] --> Validate[Final row validation]
    Command[Existing PUT PATCH batch] --> Validate
    Validate --> Index[Native atomic primary unique indexes]
    Index --> Entity[Canonical EntityRef row]
    Entity --> Graph[Graph endpoints]
    Entity --> Vector[Vector subject]
    Entity --> Events[Events and queued workflows]
```

## Implementation contract

REQ-REL-001/002/003 -> AC-AISQL-002–004/009/010 in
[RelationalStorage](../Features/RelationalStorage.md) and this ADR.
Ordered tasks:004 freezes metadata DTOs;005 writes real-ZoneTree schema/row/
atomicity/linkage regression sources then validators;008 joins configuration and
PUT/PATCH validation once per final image, client/MCP/RF3/evidence.

Root owns optional ResourceDefinition metadata and new immutable schema DTOs at
Abstractions/Features/RelationalStorage/, shared configuration/PUT/PATCH hooks,
all docs/SDK/routing joins. Worker owns only Core/Features/RelationalStorage/
validators and UnitTests/Features/RelationalStorage/ tests. Read views, physical
storage, grain ownership and existing mutation/public JSON values remain unchanged.

New resources opt in with the typed schema; changed resource schemas fail strict
configuration validation. Null metadata omission preserves the current resource
serialization. Rollout enables typed resources only where the active writer enforces
this schema. Rollback keeps persisted schema intact and fails closed whenever the
active source cannot validate or serve it; never silently discard a schema.

Tests map each column type/null/unknown/duplicate/primary condition, revision and
protected-write denial, unique conflict, full mixed-batch rollback, persistence
reopen, graph/vector row linkage, RF3 SDK/MCP SQL equivalence. CI only, no doubles.
GitHub build/format/analyzer/governance/units/recovery/RF3 and coverage evidence
join through root. Performance is validation of one final image plus existing
native indexes; speed/scale requires dedicated same-workload GitHub measurements.


## TASK-REL-UNIQUE-ROLLBACK-REPLAY-COLD-001

REQ-REL-002 / AC-AISQL-004 under ADR055 extend SAME AcAisql004PatchConflictAndDeleteRetainNativeUniqueAndRevisionContract after all original conflict, stale revision, delete and unique-name reuse assertions. This typed-row stage does not claim foreign keys/defaults/cascades/arbitrary joins/full SQL.

Original canonical state is first deleted, second revision2/namealpha/count1. A genuine Batch first stages second's count1→2 PATCH at expectedrevision2, then tries absent third row with occupied unique alpha. Exact Conflict has no successful receipt/private row; full indexed literal second row, missing third, deleted first and complete OutboxStatus remain unchanged. A definitive first rejection may persist its original failed outcome/clock; do not assert nonexistent global pre-failure byte equality. Capture complete bounded native key/value image and physical position AFTER that original failure. Retry same exact command/time returns complete original OperationResult and changes no native byte/position.

Corrected fresh command retains original PATCH and inserts third/namegamma under the SAME real unique index. Require independent complete mutation receipts patchDocument/second/revision3 + putDocument/third/revision1, native ProcessDurable, exact original commandId and real original receipt token. Full indexed alpha page is second/revision3 with independently literal canonical PATCH JSON including all typed values; gamma page is third/revision1 with exact original literal PUT JSON. Full authorized query returns those two literal rows, null cursor and actual current local cut. Same-ID full receipt replay retains all native bytes/position; original failed command still returns its original Conflict after healthy state changed, with no effects.

Join original store, same-directory native reopen with original validated options and configured factory, exact NodeId/incarnation/nondecreasing ReadGeneration/full native bytes/physical cut. Reuse both exact original commands/time: old failed result and healthy full receipt remain byte-equal without new domain effects/outcome writes. Full typed SQL indexed rows remain independently literal. Then genuine revision3→4 PATCH of second count2→3 proves cold write health and produces new independent patchDocument receipt, original new-ID replay and complete final SQL rows/outbox horizon; cold store joins before original fixture cleanup. Retain initiating and cleanup failures with original observer/lexical store ownership.

No product/API/serializer/format/limits/deadline/trust/provider change; no migration/fallback/source-oracle expectation. Core atomic gate remains authoritative. Native checkOnly + pernewpath NS/reconstruction are source only. Exact original Unit method/UID/deadline unchanged; required normal SIMD native50 compilation/formatter/analyzers/fresh discovery/runtime and Linux/process/RF3/coverage gates remain OPEN.

## Q2 retained primary identity observation

Existing nonnullable Text primary-key equality with EntityRef.Id also applies to actually observed readable nondeleted retained rows in the bounded Q2 read operator. This is the same frozen canonical identity, not schema evolution or a new constraint. Check left JSON identity before left-predicate elimination and right JSON identity before bounded candidate retention. Preserve deletion/row-ACL omission and do not inspect a right row skipped by a valid false left predicate. Native key/reference checks, ordered apply ownership, per-operation persisted permissions, read cut, budgets and cancellation remain unchanged. Existing closed Corruption identifies inconsistent retained identity. Syntactically malformed retained JSON is converted only at this bounded retained-row parser from JsonException into the same Corruption; valid non-object JSON fails the identity guard. Generic public admission/serialization failure mapping is unchanged. Independent real raw-fault/repair/GUID replay/cold operations qualify this finite rule. Source implementation joined; compiler/runtime pending and no new format/protocol or performance/qualification claim.
