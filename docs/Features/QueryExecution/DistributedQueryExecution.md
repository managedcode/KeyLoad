# KL-037 prerequisite: bounded local partition-leaf merge

Status: Accepted internal prerequisite; implementation and qualification pending.

This is a prerequisite-only internal stage. It exercises the existing Q1
operator against multiple real atomic partitions in one `TestDatabase` and
merges its bounded native leaf outputs. Every leaf remains on the same
`DatabaseEngine`/ZoneTree owner and must report that owner's actual node,
incarnation and read generation. The atomic partition is a logical unit; the
replica copies that hold it are not separate partitions. This stage adds no
route, public contract, grain fan-out, cursor, or claim of distributed
execution. Full KL-037 remains pending the Orleans partition-grain fan-out
and the later SDK/official-MCP stages.

## Requirements and acceptance

| Requirement | Acceptance | Automated evidence for this prerequisite |
|---|---|---|
| REQ-DQUERY-PREREQ-001: represent a validated bounded Q1 read plan and native leaf evidence without widening public admission | AC-DQUERY-PREREQ-001: generated internal plan accepts 1–8 unique partition leaves only; every leaf has a finite pre-granted candidate, examined-record, raw-byte and retained-byte allowance before the first leaf executes. Request version/shape, duplicate partitions, mismatched leaf/request partition, cursor, model source and inconsistent Q1 shape fail before storage reads. No caller identity or trusted role is carried in the plan. | Real ZoneTree validation tests; exact/one-over leaf-count and grant cases; generated Orleans serializer round-trip |
| REQ-DQUERY-PREREQ-002: execute each leaf through canonical local persisted authorization and expose its actual scoped evidence | AC-DQUERY-PREREQ-002: each leaf invokes the existing `QueryEngine`/`DatabaseEngine.WithQueryView` path with the authenticated principal supplied by the existing caller, reloading persisted principal, resource, row and field policy. A denial in any leaf fails the whole operation without returning earlier leaf rows. Each result retains full `EntityRef`, local cut position, policy epoch, schema version, owner node/incarnation/read generation and access path. No scalar global cut is synthesized. | Native ZoneTree positive tests across 2+ real atomic partitions; persisted denial/revocation and healthy-follow-up cases; per-leaf witness assertions |
| REQ-DQUERY-PREREQ-003: merge exact Q1 order before projection can erase sort data | AC-DQUERY-PREREQ-003: each leaf retains canonical encoded ORDER BY keys before projection. Merge compares each key using Q1 direction semantics, then breaks complete ties by ordinal `EntityRef` components `(TenantId, DatabaseId, TransactionDomainId, PartitionKey, Collection, Id)`. It emits at most `min(request.Limit, MaxResults)` rows; duplicate full references across leaves are `Corruption`. An independent central oracle compares full identity, revision, projected JSON/redaction and exact order, including a sort field omitted from projection. | Real native multi-partition store, shuffled insertion, identical textual IDs across partitions, tie and duplicate-result cases, independent in-memory seeded oracle |
| REQ-DQUERY-PREREQ-004: debit finite global work and memory grants before leaf execution/retention | AC-DQUERY-PREREQ-004: canonical request parsing/admission occurs once. The operator preallocates deterministic fixed per-leaf grants whose sums do not exceed the remaining existing `MaxScanRecords` (10,000), `MaxQueryReadBytes` (67,108,864) and `MaxBatchBytes` (8,388,608); candidates are charged/reserved before their owned order keys or projected rows are retained. Result count remains within `MaxResults` (1,000). Exact inclusive and one-over aggregate record, byte and retained-memory cases fail with `BudgetExceeded` and return no partial result. There is no work stealing or per-leaf reset of global totals. | Exact/one-over real ZoneTree reads and merge reservations; no partial rows on budget failure; later healthy query |
| REQ-DQUERY-PREREQ-005: preserve one operation token/deadline and honest leaf completion | AC-DQUERY-PREREQ-005: the existing caller token and one 30-second `QueryDeadlineSeconds` lifetime cover validation, all sequential leaf reads and merge. Caller cancellation remains cancellation; elapsed/work limits use the existing typed budget errors. Each synchronous local leaf has settled before the next starts or the method returns. During-work cancellation is asserted only through an actual native read-progress observation that cancels a real `ReadExecutionBudget`, then proves no result and a healthy follow-up. A persisted authorization/budget failure in a later leaf produces no partial page. | Real progress-observed native cancellation following existing graph/partition cancellation patterns; persisted-failure and healthy-follow-up tests |

The stage86q scalar original report establishes a test race: its async polling
monitor accepted positive bytes after the synchronous query had already returned.
TASK-DQUERY-CANCEL-OBSERVATION repairs only that test synchronization under
AC-DQUERY-PREREQ-005. Keep the same 5,000 native ZoneTree rows, request, original
CTS, 30-second operation budget and exact-token/no-result/healthy-follow-up
assertions. A QueryExecution-local native observer thread is started and signals
that it is armed BEFORE the synchronous query begins on the test thread. It
observes the actual budget byte counter, retains the observed value and requests
the same CTS on positive native work; no Task.Run/async-yield monitor races query
completion. Bound arming, observation and the initial join to 15 seconds, retain
and ultimately join the original thread before any CTS/event/store disposal,
and preserve actual primary plus observer/settlement failures with the native
ManagedCode fatal classification. A returned query, missed native progress or
failed observer is still a test failure, never a successful cancellation or
retry-to-green. No larger corpus, changed limit, fake clock/provider, product
test hook or weaker assertion is permitted. The observed native OCE and exact
caller token establish the passing flow; an armed thread alone is not that proof.
Ownership is `tests/KeyLoad.UnitTests/QueryExecution/Cases/Partition/PartitionQueryCancellationTests.cs`
and populated QueryExecution `Helpers/` for its observer/failure settlement.
ADR-100 already defines the same native synchronous/cancellation boundary;
this test-only correction adds no public, persisted or runtime contract.

### Frozen internal contract

These are internal Orleans-generated value contracts in
`KeyLoad.Query.Features.QueryExecution`; they are not published or sent to a
grain by this prerequisite. Every type has `[GenerateSerializer]`, a unique
`keyload.query.<name>.v1` alias and sequential zero-based IDs. The exact names, field order and aliases are frozen for this internal stage.

| Type / alias | Ordered fields |
|---|---|
| `PartitionQueryPlanV1` / `keyload.query.partition-query-plan.v1` | `Version:int`, `NodeId:Guid`, `Incarnation:Guid`, `ReadGeneration:long`, `ImmutableArray<PartitionQueryLeafPlanV1> Leaves`, `Limit:int`, `MaxExaminedRecords:int`, `MaxReadBytes:long`, `MaxRetainedBytes:long` |
| `PartitionQueryLeafPlanV1` / `keyload.query.partition-query-leaf-plan.v1` | `Version:int`, `Partition:PartitionRef`, `AstQueryRequest Request`, `MaxExaminedRecords:int`, `MaxReadBytes:long`, `MaxRetainedBytes:long`, `MaxCandidates:int` |
| `PartitionQueryCandidateV1` / `keyload.query.partition-query-candidate.v1` | `Version:int`, `EntityRef Reference`, `QueryRow Row`, `ImmutableArray<ImmutableArray<byte>> OrderKeys` |
| `PartitionQueryLeafResultV1` / `keyload.query.partition-query-leaf-result.v1` | `Version:int`, `Partition:PartitionRef`, `NodeId:Guid`, `Incarnation:Guid`, `ReadGeneration:long`, `CutPosition:long`, `PolicyEpoch:long`, `SchemaVersion:long`, `ExaminedRecords:int`, `ReadBytes:long`, `RetainedBytes:long`, `AccessPath:string`, `ImmutableArray<PartitionQueryCandidateV1> Candidates` |
| `PartitionQueryResultV1` / `keyload.query.partition-query-result.v1` | `Version:int`, `bool Complete`, `ImmutableArray<PartitionQueryLeafResultV1> Leaves`, `ImmutableArray<PartitionQueryCandidateV1> Rows`, `ExaminedRecords:int`, `ReadBytes:long`, `RetainedBytes:long` |

Field order is stable and version is `1`. Principal identity is supplied only
from the existing authenticated operation context; the native plan contains no
principal, role, signing key, SQL text, or policy authority. The plan rejects
any leaf whose AST differs other than its partition, whose `Partition` differs
from `Request.Partition`, or whose request has a cursor, EXPLAIN, or non-null
`ModelSource`. All leaf plans share the same Q1 query, parameters, scan opt-in
and limit. Each leaf's `Limit` is the root result limit, so the global top-L is
contained in the union of each leaf's top-L. Leaf candidate storage retains
the canonical Q1 sort bytes while the result exposes only the authorized
projected `QueryRow`.

The owner identity in this prerequisite is copied from the actual single
`DatabaseEngine.Store.Identity`, not selected by a caller. All results must
match it. `CutPosition`, `PolicyEpoch` and `SchemaVersion` are per-leaf
witnesses. Policy epochs must agree across all completed leaves; otherwise the
operator fails closed with `OwnershipLost`. Cuts may differ and are never
collapsed into a global position. An unauthorized leaf preserves the existing
`PermissionDenied` result. Invalid inputs map to `Validation`, malformed
duplicate/result identity to `Corruption`, ownership/witness mismatch to
`OwnershipLost`, budget exhaustion to `BudgetExceeded`, and cancellation to
the original `OperationCanceledException`. There is no incomplete/partial
success mode.

### Bounds and operation order

1. Validate the 1–8 leaf shape, duplicate full partitions, leaf consistency,
   common Q1 AST, cursor/model/EXPLAIN exclusions and request limits before a
   database read. Admit once using current `QueryEngine` admission.
2. Create one root operation budget/deadline and normalize or compile Q1 once.
   Allocate fixed quotient/remainder grants over the canonical ordinally sorted
   partition list. Sum of all leaf record, raw-byte and retained-byte grants
   must stay within the configured remaining aggregate budgets after request
   parsing. No leaf starts before every grant exists.
3. Execute leaves sequentially through the existing local query operator.
   Each leaf consumes its grant during native scans/lookups and before owned
   candidates are retained. Preserve the current Q1 predicate, access-path,
   field authorization, projection/redaction and local read view. Do not expose
   a `PreparedRow`/borrowed `IKeyValueView` outside its `Store.Read` callback.
4. Require identical owner identity and policy epoch. Keep one cut/policy/schema
   witness per leaf. Merge the already bounded sorted runs with a k-way
   comparator; charge merge descriptors and result retention before allocation.
   Validate the complete bounded output before returning `Complete=true`.
5. Any failure or cancellation returns no rows. Each current leaf is synchronous
   and fully settled before continuation. There is no concurrent leaf queue or
   backpressure claim in this prerequisite.

The existing limits are configured limits, not new hard-coded public promises:
query bytes 65,536; tokens 2,048; AST depth 32; deadline 30 seconds; examined
records 10,000; raw read bytes 67,108,864; rows 1,000; result/retained batch
bytes 8,388,608. Every custom lower `DatabaseLimits` setting remains effective.
Per-leaf grants are deterministic and non-borrowable. They may conservatively
reject skewed data; no grant can be multiplied by the number of leaves.

### Explicit KL-037 boundaries still open

This prerequisite cannot prove Orleans fan-out across partition grains, remote
producer backpressure/settlement, bounded node availability, SDK/official MCP
transport parity, partial-result policy, query statistics epochs or any
replicated-cluster distributed behavior. Current `QueryEngine.Execute` and
`WithQueryView` are synchronous; a deterministic slow in-flight *remote* leaf
oracle does not exist without the later native Orleans leaf route. Do not add a
fake provider, artificial delay hook, or production observer to simulate one.
The synchronous local leaf still proves persisted later-leaf failure, real
during-work cancellation, per-leaf budget exhaustion and whole-result failure.
Slow-node timeout, capacity-one native stream backpressure, original
remote-task join, and a real multi-node exact-source cluster run remain pending
and must be added only with their owning KL-037 stage.


## Source basis and ownership

The original architecture acceptance is
[`architecture-v0.3.uk.md:765-770`](../../design/architecture-v0.3.uk.md).
Current Q1 contracts are `QueryRequest`/`AstQueryRequest`, `QueryPage`, and
`QueryRow` under `src/KeyLoad.Abstractions/QueryExecution/Contracts/`.
The local operator is `src/KeyLoad.Query/QueryExecution/Queries/QueryEngine.cs`;
`PreparedQuery` owns canonical encoded order keys and currently ties on only
encoded `EntityId`; `QueryCandidateReader` applies the existing native read
budget and candidate scan. `DatabaseEngine.WithQueryView` at
`src/KeyLoad.Core/DocumentStorage/Execution/Documents.cs:107` provides
per-leaf persisted authorization and scoped native read view.
`ReadExecutionBudget` at `src/KeyLoad.Core/ReadExecutionBudget.cs` provides
current token, time, raw-byte and output bounds. `TestDatabase` opens a
real temporary `ZoneTreeStore`, bootstraps persisted root identity and exposes
its actual `DatabaseEngine`; no mock storage seam is needed.

Source ownership is limited to Query feature-local internal contracts,
models, validation/admission and executor helpers plus a narrow private
`QueryEngine` join, with real ZoneTree tests in UnitTests `Features/QueryExecution`
role folders. Query owns no storage placement or transport. The Core grant seam below enforces native leaf read ceilings; never perform
post-hoc accounting.

The exact broader KL-037 target remains in `docs/design/architecture-v0.3.uk.md`;
this file is a prerequisite-only stage. The implementation must not mark
KL-037 complete or change its status evidence. UI, SDK, MCP, SQL grammar, server
and Orleans routing are N/A for this stage.

## Canonical slice and implementation map

| Owner | Exact owned paths |
|---|---|
| QueryExecution native values | `src/KeyLoad.Query/QueryExecution/Contracts/PartitionQuery/PartitionQueryPlanV1.cs`, `PartitionQueryLeafPlanV1.cs`, `PartitionQueryCandidateV1.cs`, `PartitionQueryLeafResultV1.cs`, `PartitionQueryResultV1.cs` |
| Query validation and execution | QueryExecution feature-local `Validation/` and `Execution/`, with `Execution/PartitionQueryExecution.cs` and one internal owner accessor in `Queries/QueryEngine.cs` |
| Native regression cases | `tests/KeyLoad.UnitTests/QueryExecution/Cases/PartitionQuery*Tests.cs`, with actual fixtures and assertions in their owning role folders |
| Core grant dependency | Existing `src/KeyLoad.Core/ReadExecutionBudget.cs` and a ResourceExecution role-owned grant helper, only after its exact contract is separately frozen below |
| Durable ADR | [ADR-100](../../ADR/ADR-100-local-partition-query-merge.md) |

```mermaid
flowchart LR
    P[Authenticated local Q1 plan] --> V[One admission and normalization]
    V --> G[Finite fixed grants before any read]
    G --> L[Sequential authorized ZoneTree leaves]
    L --> W[Owned order keys and per-leaf witnesses]
    W --> M[Bounded deterministic merge]
    M --> R[Complete result or original failure]
```

This internal stage does not export a product result stream. Native Orleans
fan-out and ManagedCode.Communication streaming remain required in the later
KL-037 distributed stage before any public SDK or MCP route is delivered.

## Frozen native read-grant dependency

`ReadExecutionBudget.CreateReadGrant(long maximumBytes, int maximumRecords)` is internal and
returns the internal `ReadExecutionBudgetReadGrant`. Core exposes these only
to its explicitly declared `KeyLoad.Query` friend assembly. The helper lives
at `src/KeyLoad.Core/ResourceExecution/Execution/ReadExecution/ReadExecutionBudgetReadGrant.cs`;
its genuine native tests are `ReadExecutionBudgetGrantPointTests`,
`ReadExecutionBudgetGrantRangeTests`, `ReadExecutionBudgetGrantReservationTests`
and `ReadExecutionBudgetGrantCancellationTests` under UnitTests
ResourceExecution/Cases. No public budget API or transport is added.

Every grant is reserved before any leaf starts. Nonnegative ceilings are
accepted (zero permits only zero-charge reads); negative internal API input
is `ArgumentOutOfRangeException`. Creation checks the original root token
and deadline and uses subtractive overflow-safe admission. Accepted native
`ReadBytes` plus every outstanding reserved remainder stays within configured
`MaxQueryReadBytes`. Ordinary root `ChargeBytes` excludes those reservations
from available capacity. A native grant charge first checks original root
cancellation/deadline and its own remaining ceiling, then converts that
accepted count from reserved remainder to cumulative root `ReadBytes`, before
any visitor, copy or deserialization. Rejected charges do not mutate counters.
Unused remainder remains reserved for the operation; no later leaf borrows it.

Grant `ReadValue`/`VisitRange` invoke the actual original `IKeyValueView`
with its native pre-read charge callback and root token. Do not wrap an
already budgeted view, create another deadline/CTS or debit a read twice.
Reservation or leaf exhaustion preserves the existing safe read-byte
`BudgetExceeded` error. Ordinary root behavior is identical when no grants
exist. This stage remains synchronous and sequential and makes no concurrent
budget thread-safety claim. Exact/one-byte-under native reads prove the
visitor never executes on rejected charge, original cancellation/deadline
remains effective, and later ungranted work cannot consume reserved capacity.

The new internal leaf and merge use ordinal full-reference ties consistently
before top-L selection, including Unicode boundary cases. Existing public
Q1 `PreparedQuery` UTF-8 ID tie ordering remains its current contract.

The grant also reserves a fixed examined-attempt allowance within configured
`MaxScanRecords`. One native `StorageReadObserver` callback is one examined
attempt, including a missing point lookup and charged range lookahead. Every
callback checks both leaf ceilings before converting one reserved attempt and
its raw-byte charge into accepted totals. Either rejection mutates neither
counter. Grant `ReadBytes` and `ExaminedRecords` expose accepted work; parser
`ChargeBytes` is not an examined native attempt. Examined exhaustion has the
safe `BudgetExceeded` diagnostic "The read execution examined-record budget
is exceeded." Ordinary root behavior is unchanged when no grants exist.

## Frozen conservative retained-byte model

This model bounds logical retained query state and does not promise process
RSS. Constants are: pointer slot8, array descriptor32, string descriptor32,
candidate/reference/projected-row descriptors256, heap entry64 and seen-full-
reference entry64 bytes. Use checked or subtractive overflow-safe arithmetic.

A candidate costs256 plus `(32 + 2 * Length)` for each of its six full
EntityRef strings, `(32 + 2 * Row.Json.Length)`,
`(32 + 8 * Row.RedactedFields.Length)` plus `(32 + 2 * Length)` for each
redacted field string, and `(32 + 8 * OrderKeys.Length)` plus
`(32 + key.Length)` for each canonical order byte array. Row.Id reuses
Reference.Id and is not charged again. Repeated field strings may make this
logical model conservative even when native objects share storage.

Before fixed per-leaf retention grants are divided, reserve128 root descriptor
bytes,256 per leaf,64 per total planned MaxCandidates for the full-reference
seen set,64 per leaf for merge heap entries, and32+8*rootLimit for final row
slots. Each leaf reserves32+8*(L+1) heap/scratch reference slots and charges
owned candidate payload before retaining it in top-L, releasing rejected or
evicted payload. Before allocating the final leaf candidate array, reserve
its own32+8*n bytes while the scratch heap is still held; relinquish scratch
charges only after that helper relinquishes its containers. Final retained
totals cover surviving payload and owned leaf/result containers. Leaf result
candidates and merged Rows reference the same objects: row-slot arrays cost
bytes, candidate payload is never charged as a second allocation.

The existing canonical KeyCodec remains unchanged. Only one temporary
candidate's encoding/projection is present at once, bounded by admitted native
raw bytes and configured document limits; that transient bound is distinct
from final retained accounting. This stage makes no peak-RSS claim. Exact and
one-under tests compute the frozen retained formula independently from their
scalar seeds, including Unicode, escaped JSON and redacted fields.

The internal leaf top-L uses one custom binary heap backed by exactly L+1
reference slots. It compares canonical order bytes and ordinal full EntityRef
before admission; the public Q1 PriorityQueue and its existing UTF-8 EntityId
tie contract remain unchanged. Compute and reserve the exact owned candidate
payload before inserting it. Reservation failure leaves the previous heap and
output unchanged. Before transferring candidates, reserve the final array
while heap scratch is still held, clear and relinquish every heap reference,
then release its scratch reservation. One callback-local temporary candidate
is bounded by admitted raw bytes and document limits; it is not retained output.



## TASK-PQUERY-PUBLIC: public multi-partition SDK/MCP contract

Status: contract accepted before implementation; source integration and all runtime qualification remain open. This stage does not complete KL-037 distributed execution.

This stage publishes a typed multi-partition request over the already implemented bounded partition-leaf merge. The caller's connection grain ([ADR-125](../../ADR/ADR-125-connection-owned-execution.md)) executes the request: it calls the logical partition grains through ordinary Orleans calls, each leaf runs against its ZoneTree owner, and the connection grain merges the leaves into one page. The stage adds no global transaction snapshot, cursors or SQL grammar. The cluster concept is described in [ADR-136](../../ADR/ADR-136-native-orleans-partition-ownership.md).

## REQ/AC

- REQ-PQUERY-001: expose a bounded generated request that carries only up to eight full atomic `PartitionRef`s, one existing `SelectQuery`, existing query parameters, full-scan opt-in, and AST version. AC-PQUERY-001: reject default/empty/over-eight or duplicate partitions, invalid AST version/shape, oversized request, cursor-bearing or otherwise unsupported query semantics, `Explain`, and non-null `ModelSource` before any storage read; do not accept caller identity, roles, cut, read grant, or deadline.
- REQ-PQUERY-002: retain the native persisted-authority path. AC-PQUERY-002: each SDK/MCP call follows signed request verification, native request identity validation, bounded connection-grain admission, request freshness validation, persisted principal and policy reload, and normal query-capability execution. No admin-only API is invoked for a non-admin.
- REQ-PQUERY-003: bind each authorized leaf to its own scoped read cut. AC-PQUERY-003: every leaf executes inside the existing `WithQueryView` callback, after it has loaded the persisted principal and passed `Authorization.Require(Query | DocumentsRead)` for that leaf's partition/collection, and reports its own cut, policy epoch, schema version and access path. Leaves may differ in cut and access path; policy epochs must agree. An unauthorized leaf fails `PermissionDenied`, an inconsistent witness fails `Corruption` or `OwnershipLost`, and no partial page is returned.
- REQ-PQUERY-004: reuse exact DQUERY r2 semantics and budgets. AC-PQUERY-004: one admission/normalization/compilation and one original `ReadExecutionBudget`/caller token/deadline cover all leaves and merge. Reserve the complete 1-8 leaf grants before any read; each leaf rechecks persisted principal/resource/row/field policy, the local store identity/read-generation and the policy epoch. Any later denial or witness mismatch returns no rows. Preserve separate per-leaf cut positions; never synthesize a scalar global cut. Keep the internal DQUERY exact comparator, full EntityRef tie-break, candidate/work/read/retained bounds and no partial result.
- REQ-PQUERY-005: expose only complete typed replies through the existing native CQRS stream. AC-PQUERY-005: the connection grain returns one typed `PartitionQueryPageV1` after all leaves and the merge settle; faults/cancellation dispose/join the actual stream under existing native lifecycle rules. SDK and official MCP logical results match; no second dispatcher, unbounded materialization, or partial-page mode.
- REQ-PQUERY-006: qualify the public capability on the real Aspire-owned replicated cluster. AC-PQUERY-006: direct .NET SDK and official MCP cover independent ordered oracle, same textual IDs in distinct partitions, hidden sort fields, ties, empty/no-hit partitions, persisted denial/revocation on a later leaf, caller cancellation/limits with healthy follow-up, acknowledged write/read through a surviving node, and malformed/version/oversized input. The real official MCP schema oracle checks exact request/result properties and required fields; nested `PartitionRef` includes computed `atomicPartitionId` as an optional schema property in addition to its four required identity strings. For nullable `Parameters: Dictionary<string, JsonElement>?`, the type oracle admits only scalar `object`, singleton type-array `[object]`, or the exact unordered two-member set `{object, null}`; the latter is permitted only at this nullable dictionary site. Non-nullable schema fields accept only their exact scalar or singleton type array. Extra, duplicate, empty, non-string, or contradictory type arrays fail. Local references remain bounded to depth 8. Ordered tie fixtures use the native non-unique rank index without changing duplicate-rank inputs; separate native unique-index rejection cases remain mandatory. Every row with `Redacted == false` has a present, initialized `RedactedFields` collection with zero entries; a null collection is not equivalent. Rows that redact data retain the exact omitted field paths. Evidence does not claim full KL-037 completion.

## Public generated contracts

The frozen public error map distinguishes the request envelope from the AST: an unsupported `PartitionQueryRequestV1.Version` returns `Validation`; an unsupported `AstVersion` returns `UnsupportedCapability`. Neither returns a successful partial page. Empty, duplicate or over-eight leaves are `Validation`; configured request/result/retained limits are `BudgetExceeded`.

All contracts live in `KeyLoad` alongside current query contracts; aliases are feature-local named constants in Abstractions `Features/QueryExecution/Serialization`, not a new global alias layer.

- `PartitionQueryRequestV1`, alias `keyload.contract.partition-query-request.v1`: `[Id(0)] int Version` (must be 1); `[Id(1)] ImmutableArray<PartitionRef> Partitions`; `[Id(2)] SelectQuery Query`; `[Id(3)] Dictionary<string, JsonElement>? Parameters`; `[Id(4)] bool AllowFullScan`; `[Id(5)] int AstVersion` (must be 1). `SelectQuery.Limit` remains the sole row-limit field. Cursor, explanation, and model-source are absent/rejected. The server caps partitions at 8 and enforces the current configured query/result/body/read/scan/retention limits; no caller-settable work grant exists.
- `PartitionQueryRowV1`, alias `keyload.contract.partition-query-row.v1`: `[Id(0)] EntityRef Reference`; `[Id(1)] QueryRow Row`. The full reference distinguishes equal textual IDs in different partitions. No canonical sort bytes or unprojected record is public.
- `PartitionQueryLeafWitnessV1`, alias `keyload.contract.partition-query-leaf-witness.v1`: `[Id(0)] PartitionRef Partition`; `[Id(1)] long CutPosition`; `[Id(2)] long PolicyEpoch`; `[Id(3)] long SchemaVersion`; `[Id(4)] string AccessPath`. The local store's NodeId/incarnation/read-generation are validated internally and are not exposed. These are per-leaf witnesses and can differ in cut/schema/access path; policy epoch and actual store identity must agree.
- `PartitionQueryPageV1`, alias `keyload.contract.partition-query-page.v1`: `[Id(0)] int Version`; `[Id(1)] ImmutableArray<PartitionQueryRowV1> Rows`; `[Id(2)] ImmutableArray<PartitionQueryLeafWitnessV1> Leaves`; `[Id(3)] bool Complete`. Every successful reply has `Complete=true`; all failure paths return no rows and there is no incomplete mode.

## Ordered ownership and joins

1. Contract review freezes aliases/IDs/error map/response witnesses and explicitly says no global cut claim.
2. Query owns the public generated DTOs/aliases, bounded request validation, mapping to the existing internal `PartitionQueryExecution`, and projection from internal candidates to the complete public page. It retains the existing QueryEngine/one-budget path; no second admission/deadline.
3. Root owns append-only `GrainReadKind.PartitionQuery` and `GrainQueryReadCapabilities` binding inside `DatabaseReadGrain`, existing request-envelope/payload validation and capability inventory; no second grain/dispatcher.
4. Root owns HTTP `POST /v1/query/partitions`, SDK `PartitionQueryAsync(PartitionQueryRequestV1, CancellationToken)`, MCP `keyload_query_partitions` at `/v1/query/partitions` with read-only hints/catalog description, and typed response mapping, all through the normal signed read path.
5. Add generated-native codec/strict public-input cases; real ZoneTree unit tests; then genuine Aspire-owned cluster .NET SDK + official MCP tests. Build/format/analyzers and complete unit/recovery/cluster gates precede any acceptance claim.

## Source-grounded current joins

- `SelectQuery` is generated native Q1 and already owns `Limit` (IDs 0–7; `QueryAst.cs`); `AstQueryRequest` has one `Partition`, optional `Cursor`, and `AstVersion` (IDs 0–5). The proposed public request deliberately does not embed it.
- `PartitionQueryExecution.ExecutePartitionQuery` is internal and already creates one `ReadExecutionBudget`, admits once, normalizes once, reserves all grants, executes sequential real local leaves and merges complete output.
- `PartitionQueryLeafExecutor.Execute` calls existing `DatabaseEngine.WithQueryView`, whose callback runs only after persisted principal/resource authorization. It captures owner identity and per-leaf cut/policy/schema/access-path witnesses.
- `DatabaseReadGrain` currently owns signed validation, native read admission, one quorum barrier, freshness and persisted authority reload before `GrainQueryReadCapabilities.ExecuteAsync`; the new kind must be appended without changing existing numeric enum values.
- Existing transport joins are `QueryApi.Map`, `KeyLoadClient`, `McpReadCatalog`, `McpToolNames`, `McpToolRoutes`, `McpToolHints`, and `ApiGrainDispatch`. Root owns all of them.

## Public mapping

The public mapper has this exact operation shape: `PartitionQueryPageV1 MapPublic(PartitionQueryResultV1 result, DatabaseLimits limits, ReadExecutionBudget budget)`. It computes and admits the checked public mapping retention *before allocating* page arrays, row wrappers, or witness records, while the internal candidate buffers and their sort keys are still held. The conservative logical additional charge is named and frozen as: page descriptor64; each returned-row array uses descriptor32 plus8 per reference and each new row wrapper costs32; each leaf-witness array uses descriptor32 plus8 per reference and each new witness wrapper costs64. `EntityRef`, `QueryRow`, their strings, and the existing partition/access-path values are references reused from the already owned internal result; no payload/sort-key/JSON bytes are copied by the mapper. Checked sum of `result.RetainedBytes` plus this additional mapping charge must remain ≤ configured `MaxBatchBytes` before allocation; otherwise throw typed `BudgetExceeded` with no page. After mapping, call the existing `budget.CheckResult(publicPage)` to enforce serialized response-byte bounds and cancellation. This is an intentional conservative overlap reservation while internal source buffers remain live; it makes no process-RSS claim, opens no second budget/deadline, and leaves the internal result unreachable after return.

The final public page field order is fixed as accepted: Version `[0]`, Rows `[1]`, Leaves `[2]`, Complete `[3]`. Exact aliases and IDs in the preceding tables remain unchanged.

### Acceptance mapping

`PartitionQueryExecution.ExecutePartitionQuery` retains its existing internal calls unchanged. Public normalization validates every requested partition first and uses the first canonical sorted partition as the server-generated `AstQueryRequest.Partition` anchor. All leaves remain in the plan and are authorized separately.

REQ/AC-PQUERY-001/004/005 map to `PartitionQueryPublicContractTests`, `PartitionQueryPublicBudgetTests` and `PartitionQueryPublicMergeTests` under `tests/KeyLoad.UnitTests/QueryExecution/Cases/`; REQ/AC-PQUERY-002/003 map to `PartitionQueryPublicAuthorizationTests`; AC-PQUERY-006 maps to `PartitionQueryPublicRf3Tests` under `tests/KeyLoad.IntegrationTests/QueryExecution/Cases/`. Root joins the append-only native read kind, persisted authority path, signed SDK/HTTP/MCP route and complete independent MCP inventory/schema/native-codec corpus. All of these tests use the Aspire-owned caller. A focused pass is development evidence; original full Linux unit/scalar/recovery/cluster/fault/endurance evidence remains required.

### Complete-flow contract regressions, 2026-10-05

TASK-PQUERY-FUNCTIONAL-CONTRACT-001 strengthens the existing seven
`PartitionQueryMcpContractTests` under REQ/AC-PQUERY-001/005 and
REQ-CQ-009/AC-CQ-021. Each positive test configures an actual persisted collection,
commits a literal document to genuine ZoneTree, decodes the real native/public
request, executes `QueryEngine.QueryPartitions`, and checks the full returned
reference, projected content, persisted revision, leaf witness and unchanged
committed state. Alias/field-ID, schema/effect-hint and writer equality checks
remain additional assertions inside those operations. Page serialization consumes
the actual returned page, never a constructed successful reply.

The canonical MCP request without full-scan permission executes and rejects with
`UnsupportedCapability`, preserving the stored document and position. Decode a
request with explicit permission through the same catalog and execute a healthy
follow-up on that same store. Invalid envelope/AST versions retain their actual
pre-storage rejection, exact errors, zero storage reads and unchanged position.
Keep the existing test names and normal/scalar contributor identity. These local
native-codec flows do not replace the official MCP/SDK cluster AC-PQUERY-006 cases.

Canonical MCP AST/partition-query fixtures use a valid RFC6901 field pointer
while preserving their identifier alias. A bare identifier path is malformed
input and must not mask the intended full-scan denial. The same decoded canonical
request must pass normal validation, produce the specified consent rejection,
then return the complete persisted row after explicit consent; correcting fixture
input does not weaken production validation or change its error contract.

Ownership: existing ClientApi/Cases/PartitionQueryMcpContractTests.cs and
ClientApi/Fixtures/Distribution/PartitionQueryMcpTestData.cs in the QueryExecution test slice;
production contracts, aliases, field IDs and execution paths are unchanged.
ADR: the existing ADR-100 public-query contract is sufficient; no new boundary,
data format or transport is introduced. First complete source review and the
strict Release build, then run the normal/scalar functional profile through
Aspire with fresh source/DLL/PDB/contributor guards and retain its original native
reports. Actual collection and complete mandatory suites remain acceptance gates.

## Bounded parallel leaf execution

REQ/AC-DQUERY-PREREQ-005 and REQ/AC-PQUERY-004/005/006, ADR-100: the centrally validated downward `MaximumConcurrentPartitionLeaves` defaults to 2, has ceiling 8 and never exceeds the actual plan leaves, `MaximumPartitions` or existing admissions. Complete original per-leaf grants and fixed result slots are allocated before dispatch. Grant reservation is serialized using the original parent budget. A prepared leaf owns only an immutable bounded request and its own call; it does not receive or mutate the parent budget, grant or read view while running. Each leaf continues with its own downward budget and the same authorized native cut. No shared read view or mutable aggregate ledger is passed concurrently.

A bounded batch admits at most the configured concurrency. Leaf calls start independently as ordinary Orleans grain calls, never through an alternate dispatcher. All leaf terminal replies and cleanup settle before serialized validation/import/retention. Closed per-leaf result slots and original caps bound simultaneous buffering. Caller cancellation or the first admitted leaf failure stops further batches, cancels all admitted work with the original linked operation token, and joins every admitted leaf; every primary/cleanup failure is retained. No partial public page; all leaves must succeed before the existing deterministic merge and public mapping. Deadline semantics and the original caller token remain unchanged; cancellation classification never masks additional cleanup faults.

Whole flows: independent ZoneTree stores, concurrent leaf work under the owning injected clock and native observable read phase, original caller cancellation with joined workers and unchanged full bytes/cuts and a null page, then a complete literal healthy merge. The Aspire-owned cluster SDK/official MCP/Q1 flow cancels only after actual leaf observation, joins the producer and original clients, and performs a fresh literal healthy merge. No sleeps/retries/quota increase/fake clients/provider/test hook; performance/resource scalability remains unqualified pending comparable Linux GitHub evidence.

## TASK-KL037-GLOBAL-MODALITY-001 - distributed statistics and modality execution

Pre-implementation owning contract. REQ-DQUERY-GLOBAL-001 / AC-DQUERY-GLOBAL-001 requires exact lexical and vector candidate windows from every queried logical partition before weightedRrfV1 fusion. It reuses native TextRanker BM25 arithmetic, the VectorRanker metric/scalar/SIMD contract, GlobalBranchWindowMerger full-reference order and SearchRankFusion. Local fused top-k cannot substitute per-modality global windows. The exact profile rejects a candidate window that is truncated, incomplete, approximate, incomparable or exceeds its original grant; there is no silent exact claim. Approximate ANN/provider profiles remain explicitly unsupported by this exact route and their separate existing APIs remain unchanged. No distributed graph, aggregate SQL or ANN approximation completion is inferred.

REQ-DQUERY-GLOBAL-002 / AC-DQUERY-GLOBAL-002 requires one server-derived statistics epoch over the exact ordered vector of actual partition cuts, principal policy epochs, resource schemas and query-term/profile identity. Each partition captures its actual authorized visible document count (including documents without the text field), total canonical token length and per-query-term document frequency. The connection grain sums these finite native summaries with checked arithmetic, using canonical query-term order and distinct full atomic partitions. Every candidate phase revalidates its original partition cut and current persisted authority before applying the shared global BM25 values. Changed cuts/policy/generation refuse `OwnershipLost` with no partial page; no automatic recapture/retry. The epoch is ephemeral operation evidence, not persisted global coordinator authority or a caller-selected freshness token. Independent partition cuts are never equated or replaced by a scalar global cut. ACL-visible corpus statistics cannot be returned publicly.

REQ-DQUERY-GLOBAL-003 / AC-DQUERY-GLOBAL-003 requires all effects and read phases to run through the existing server-owned connection grain with freshly signed bounded call-local read work and native ManagedCode.Communication streaming. The connection grain authorizes every partition/collection first; each partition leaf reloads the same authenticated subject from persisted database state and enforces Query/DocumentsRead, vector and field policy before reads. No roles, grants, raw credentials, caller statistics or borrowed storage views are accepted. Default existing PartitionQuery Q1 request validation and ModelSource rejection remain unchanged.

REQ-DQUERY-GLOBAL-004 / AC-DQUERY-GLOBAL-004 reserves the complete statistics, modality-candidate, selected projection and final revalidation grants before any dispatch using the original centrally validated MaximumPartitions/MaximumConcurrentPartitionLeaves and DatabaseLimits scan/raw/retained/result ceilings. All phase work shares one original token/deadline; no per-phase reset, work stealing, new quota or limit. Measured typed summaries, candidates and selected documents are charged before retaining/encoding/allocating. A bounded batch retains original tasks until all settle; first failure cancels all admitted leaves, stops later phases and joins all readers/providers before response/store shutdown. No partial success; server-only statistics or raw secret fields never enter public output, hints or logs.

REQ-DQUERY-GLOBAL-005 / AC-DQUERY-GLOBAL-005 requires actual multi-partition ZoneTree flows and the Aspire-owned cluster flow: independently centralized lexical/vector/fusion literal oracle; ties/identical text IDs under distinct full references; empty/nonmatching/missing fields/skew; exact and one-over budgets; changed statistics/cut/policy and leaf denial; actual in-flight cancellation/deadline followed by unchanged full canonical images/independent cuts and healthy continuation; same-root cold restart/replay; all SDK/official MCP/Q1 caller/schema/privacy parity. No benchmark, power-loss, resource or performance claim is supplied by source.

### Stable native component contracts

`DistributedTextStatisticsV1` alias `keyload.query.distributed-text-statistics.v1`, IDs0 Terms:ImmutableArray<string>,1 DocumentCount:int,2 TotalLength:long,3 DocumentFrequencies:ImmutableArray<int>. Version belongs to the encompassing typed request; this statistics record has no identity/roles/epoch supplied by callers. Its actual Terms are produced by existing canonical SearchTerms under native execution limits, and every value is validated/charged. Existing native/public aliases and IDs are unchanged. TextRanker local Rank continues its original field-based arithmetic without a new default allocation; global Rank consumes exactly this validated summary through the SAME scoring body.

Subsequent source shipment must freeze complete private statistics/candidate/projection request/result aliases and IDs plus the additive public DistributedSearch route/schema tuple before their code. No partial ingress is joined alone. Stages: actual native statistics/scoring component, same-view exact-cut leaf, authorized multi-partition coordinator in the connection grain, public SDK/MCP/Q1 composition, independent Unit/process/cluster oracles, fresh root-owned census/build/Linux acceptance. Rollback withdraws only the additive operation and removes disposable stats; no persistent current format or migration fallback is introduced.

### Per-leaf statistics witness

Each partition leaf captures its witness (partition, node, read generation, cut position, policy epoch, schema version, resource policy digest and text statistics) from the actual `WithPartitionQueryFenceView` read, after fresh persisted Query/DocumentsRead/field authorization. The complete current native store identity and resource digest/cut must match before any candidate read; absence or change refuses, never reopening an old cut. The common opaque epoch is the canonical SHA256 of the bounded native generated epoch input over all leaf witnesses. Statistics capture and candidate copies reserve the existing PartitionQueryRetention array/string descriptors and pointer/primitive sizes before cloning; native measurement and the original budget remain mandatory. No new hard-coded operational cap. Returned terms are canonicalized by the actual TextRanker; local Rank() still avoids the new DTO allocation and delegates the same exact arithmetic. No identity, secret canary, statistics count or resource digest is published in the public epoch value.

### Per-phase accounting and candidate contract

The statistics witness appends Id9 ReadBytes:long and Id10 ExaminedRecords:int from the actual entered native read grant. The parent imports these original counters before retention and progression, while charged retention/result frames remain independently admitted under original bounds. Statistics-only TextRanker construction retains no candidate corpus; it preserves the exact native tokenizer/frequency/length path. Each receiver statistics leaf owns the original database.AdmitQuery permit once for the complete synchronous operation.

Vector-only scope records the actual authorized visible canonical document count with no selected text terms/frequencies or text length, rather than fabricating a text query/field. Fresh VectorSearch and vector-field-use policy precedes capture; hybrid scope requires both actual field capabilities. Empty normalized text terms in statistics-only construction still measure the selected text field corpus; old local empty-text ranking remains unchanged.

DistributedSearchCandidateLeafV1 alias keyload.query.distributed-search-candidate-leaf.v1: Id0 original statistics witness, Id1 nullable actual text GlobalBranchWindow, Id2 nullable actual vector GlobalBranchWindow, Id3 actual phase native ReadBytes, Id4 actual phase native ExaminedRecords. Existing GlobalBranchWindow/Candidate/Scope aliases and fields are reused unchanged. Full source windows are captured only after successful complete canonical scans; each score is rebound at that exact cut to its actual full document reference/revision before bounded retention, then sorted with existing full-reference GlobalBranchOrder. No leaf top-k discards candidates before global per-modality merge.

The existing PartitionQueryParallelBatch remains the task owner. Its exact original WhenAll/sibling-cancellation/primary+cleanup join body is factored once into a typed generic overload; Q1 delegates through a static noncapturing runner with one original task array. New typed phases reuse that same bounded owner, not a new dispatcher or scheduling authority. Supporting native tests execute actual multi-partition statistics/ranking/projection, an actual committed policy-epoch failure, fresh complete literal results and owning cold reopen. These are supporting operation tests, not public cluster qualification.

### Additive public operation boundary

`DistributedSearchRequestV1` alias `keyload.distributed-search-request.v1`, IDs0 Version:int,1 Partitions:ImmutableArray<PartitionRef>,2 Search:SearchRequest. The EXISTING SearchRequest is the one common caller-visible typed search shape/defaults/weights/vector/allowlist/explain contract; its logical template partition must equal the first declared partition, all partitions are unique and bounded by existing QueryExecutionOptions.MaximumPartitions, and each internal leaf replaces only that logical partition. No copied defaults, owner proofs, identity, roles, statistics, expiry or grants are accepted. Version1 only. TextIndex selection is explicitly UnsupportedCapability in this canonical distributed profile; existing selected Search/ANN APIs remain unchanged.

`DistributedSearchPageV1` alias `keyload.distributed-search-page.v1`, IDs0 Version:int,1 Hits:ImmutableArray<RankedDocument>,2 Leaves:ImmutableArray<PartitionQueryLeafWitnessV1>,3 StatisticsEpoch:string,4 Complete:bool. Hits are actual fully projected same-cut documents with independent full references/revisions/redaction and weightedRrfV1 scores/explanation. Leaves reuse the existing public scoped per-partition cut/policy/schema/access-path witness, without exposing private proof or resource-policy bytes. StatisticsEpoch is opaque descriptive evidence only and cannot authorize another read. Complete means exact requested top-k over complete global per-modality windows; no partial/approximate candidate result is admitted by this profile.

New public read name `DistributedSearch` maps the same typed request/result across native SDK, official MCP and Q1 CALL, under current ADR125 bounded connection-owned call-local/ManagedCode.Communication read execution. Shared GrainReadKind append composes AFTER KL039 OnlineTextMaintenance, and current connection-owned capability/codec/catalog additions compose against KL039 exact immutable ancestors. Root owns independent complete literal catalog integration and current native metadata count; no count is guessed here. No partial public ingress is integrated alone.

### Receiving phases and final projection

ADR-125 governs execution: ingress and every leaf call retain independent signed call identities, current persisted authorization, context, cancellation and joined work in the existing bounded connection-grain/call-local pipeline. No completed call history or new activation per call is introduced.

The private leaf phases are Statistics, Candidates, Projection and Revalidate. Original centrally validated bounds and the operation deadline constrain every phase; no lifetime reset or public phase input exists. Before any result the projection leaf freshly requires the same cut/current principal/resource and all original text/vector field capabilities, then checks each selected full reference and actual canonical document revision. No deleted/revised/foreign selected document is projected. Explain is attached from the parent's globally fused rank contributions, not generated by an unrelated local ranking pass. Final revalidation repeats actual current modality capabilities and the original witnesses before the response.

Canonical-only leaves call the original TextRanker scoring kernel directly, so SearchBranchExecution/KL039 selected-reader lifecycle is not replaced. The provisional shared rank overload is excluded. The existing GlobalBranchWindowMerger retains its original MaxResults bound; if a complete globally ranked branch would be truncated there, the exact operation refuses BudgetExceeded before fusion instead of silently using a local/global top-k approximation or adding a larger cap. Existing original Search/ANN profiles and defaults remain unchanged.

### Complete caller schema and canonical SQL delegation

The public current bounded profile adds tool literal keyload_query_distributed_search and POST /v1/query/distributed-search, input exactly DistributedSearchRequestV1 {version,partitions,search}, output exactly DistributedSearchPageV1 {version,hits,leaves,statisticsEpoch,complete}. Nested search retains all existing SearchRequest fields/defaults and explicit selected native generation is UnsupportedCapability for this new canonical corpus profile. SDK DistributedSearchAsync, official MCP descriptor and Q1 CALL keyload_query_distributed_search(@payload) all delegate to this same read kind through the existing canonical gateway and bounded discovery; no new SQL parser/dispatcher, extra public privileged field, cursor or global-cut claim. Hints are ReadOnly=true, Idempotent=true, Destructive=false. Initial gateway discovery remains unchanged. All old catalog entries retain exact schemas/routes/hints; independent Unit+officialSDK schema/literal expectations must add this tuple separately before whole join. Two private appended read kinds follow actual current OnlineTextMaintenance and preserve all previous ordinals.

### TASK-KL037-CATALOG-DECODE-001 - complete native schema/decoder continuation

REQ-DQUERY-GLOBAL-001/003/005 and AC-DQUERY-GLOBAL-001/003/005 plus AC-MCP001 map to the existing `McpCatalogTests.AcMcp001EveryFrozenOperationHasItsExactRouteAndOneCapability`. Preserve its complete independent inventory; append feature-local distributed-search assertions for the literal tool/route/read capability/hints, strict public version/partitions/search schema (all fourteen existing nested fields and optional Limit), complete five-field result and nested document/explanation/scoped witness schema. Execute actual public JSON to descriptor decode to generated native request and public roundtrip, reject caller-authority/unknown outer and nested fields, and perform healthy decode after each refusal. Complete independent result literals must roundtrip through actual public JSON and generated native serializers. These prove schema/decoder behavior only; the separately mapped true two-owner SDK/official MCP/Q1/cold cases and exact-source Linux native outcomes remain required and unqualified until executed. No catalog count, discovery or PASS is inferred from this source.

### Original-deadline normalization

REQ/AC-DQUERY-PREREQ-005 and REQ/AC-DQUERY-GLOBAL-004/005 distinguish actual caller cancellation from elapsed query budget. The existing Coordinator owns one unchanged QueryDeadlineSeconds timer and already maps its OperationCanceledException to BudgetExceeded with the exact safe deadline detail. A receiving native stream can instead return a typed Cancelled; all original children settle before the owning batch exposes that exception. A batch can preserve multiple failures as an aggregate.

Proposed owning mapping is admitted only when the same timer has actually fired, the original caller token is not canceled, and every leaf in the original nonempty exception tree is a typed KeyLoadException with Code=Cancelled. The original direct OperationCanceledException catches remain unchanged; arbitrary aggregate cancellation/cleanup exceptions are not newly admitted. It never maps mixed permission/ownership/corruption/fatal/cleanup failures. The entire original caught tree must remain as the mapped exception cause, with no changed public error fields, aliases or IDs. True caller cancellation takes precedence. No fresh timer, retry, observer authority or remote-clock deduction.

The existing exact ErrorCode factory lacks a cause-bearing overload. A shared additive factory/constructor accessibility seam is approved by root before source.

Root-approved shared seam: the existing four-argument KeyLoadException constructor becomes internal; Errors.Fail(ErrorCode code, string detail, Exception cause) checks the cause and calls that same constructor with Errors.Status(code). Every old overload, native alias/Ids, code/status and public safe problem detail remains unchanged. Existing original direct OCE catches are not rewritten. The typed nonempty cancellation tree is mapped only by the owning Coordinator after all tasks have joined.

### TASK-KL037-SHARED-ERROR-OWNER-001 - bounded shared primitive responsibility

Root approves exact extraction of entire existing KeyLoadException and Errors types from Abstractions/Contracts.cs into Abstractions/KeyLoadException.cs and Abstractions/Errors.cs respectively. These are solution-wide shared primitive owners under the existing root building-block exception, not feature layers or partial files. Preserve namespace KeyLoad, all original members/bodies, native Alias/field IDs and ABI except the already approved internal four-argument constructor and additive cause factory. Contracts remains below400; each extracted type below200. No friend assembly, rule exemption or binary format/JSON change. Integration is one guarded atomic source cohort; rollback is the exact original source cohort before compilation/publication, not a persisted-state migration. Native checkOnly preview/new-path NotSupported and whole canonical build plus same original deadline/caller/mixed-operation controls are required; source movement is not runtime proof.

The all-typed-cancellation classifier inspects every aggregate child and every non-null typed cancellation InnerException, not just the outer code. An empty aggregate or any nested permission/ownership/corruption/fatal/cleanup leaf refuses normalization. A failure-path-only explicit stack avoids recursive CLR stack growth; the original tree is never rewritten or flattened for storage. The production default success path allocates no classifier state. The native decision control additionally wraps its actual persisted denial in the approved cause-bearing cancellation factory and verifies that nested denial cannot be hidden by an outer Cancelled code.
