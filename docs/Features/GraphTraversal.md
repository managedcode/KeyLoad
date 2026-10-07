# GraphTraversal

The accepted [ShortestPath](GraphTraversal/ShortestPath.md) contract and
[ADR-098](../ADR/ADR-098-bounded-graph-paths.md) add the missing KL-023 unweighted
path/frontier and shared SQL/SDK/MCP stages. Implementation and full qualification
are pending; the reachability oracle does not close those requirements.

Status: bounded same-partition graph mutation and traversal are present in Core source; broader graph/search integration and GitHub qualification remain pending. The target design is summarized in [sections 8, 26, 28, and 35](../design/architecture-v0.3.uk.md).

## Purpose, actors, and entry points

GraphTraversal owns graph edge records, adjacency maintenance, and authorized bounded directed traversal over document vertices. Actors are database clients and authorized query/search operators. Current writes are `UpsertEdge`/`DeleteEdge` mutations handled by `DatabaseEngine`; current reads call `DatabaseEngine.Traverse` with a `TraverseRequest`. Contracts are in `src/KeyLoad.Abstractions/Contracts.cs` and `Queries.cs`; implementation is in `src/KeyLoad.Core/GraphAndSeries.cs`.

## Canonical slice map and boundaries

| Surface | Current source | Target owner |
|---|---|---|
| Contracts | `src/KeyLoad.Abstractions/Contracts.cs`, `Queries.cs` | `src/KeyLoad.Abstractions/Features/GraphTraversal/` |
| Backend | `src/KeyLoad.Core/GraphAndSeries.cs`, mutation dispatch/authorization in `DatabaseEngine.cs` and `MutationAuthorization.cs` | `src/KeyLoad.Core/Features/GraphTraversal/` |
| Tests | `tests/KeyLoad.UnitTests/GraphAndSearchTests.cs`, `ReadExecutionTests.cs`, `HttpAdmissionTests.cs` | `tests/KeyLoad.UnitTests/Features/GraphTraversal/` |
| Durable specification | This file | `docs/Features/GraphTraversal.md` |
| HTTP | `src/KeyLoad.Server/ApiEndpoints.cs`: `POST /v1/graph/traverse`; edge mutations enter through shared `POST /v1/commands` | Shared HTTP transport belongs to `src/KeyLoad.Server/Features/ClientApi/`; graph semantics belong to `src/KeyLoad.Core/Features/GraphTraversal/` |
| .NET SDK | `src/KeyLoad.Client/KeyLoadClient.cs`: `TraverseAsync` and `CommitAsync` for graph mutations | Shared client transport belongs to `src/KeyLoad.Client/Features/ClientApi/`; typed graph behavior maps to this GraphTraversal slice |
| Official MCP | The authenticated official C# SDK exposes `keyload_graph_incoming` and shortest-path tools through the shared ClientApi gateway/catalog | Real RF3 schema oracles preserve exact properties/required fields and hints; nested `PartitionRef` includes computed `atomicPartitionId` as an optional property |
| UI | No dedicated graph-management or traversal frontend is specified | N/A: graph traversal is a database/API query capability, not a separate frontend surface |
| External graph provider | None is selected or implemented for canonical edge ownership | N/A: provider-backed/ranked graph retrieval is separate planned search work |

Current layer-first locations are migration debt under ADR-032. GraphTraversal does not own document CRUD, physical placement, query ranking, or vector/text providers. Graph vertices are references to visible collection documents; graph and vertex permissions remain distinct checks.

## Accepted TASK-MP-007C resource repair

[ADR-035](../ADR/ADR-035-memory-performance.md) adds REQ-GRAPH-005, mapped to
AC-GRAPH-002/003/004 and AC-MP-005/006/012: reuse vertex visibility and collection
authorization inside one read cut without retaining full vertex documents.
Cache only boolean visibility by complete EntityRef and authorized/unavailable
collection decisions by complete partition plus collection; never a cross-request
or payload cache. A denied/hidden target stays excluded and cannot be expanded.
The start's existing visible/permission errors still propagate.

Start one cancellation/operation-clock budget before the gate. Visit adjacency
records directly without a raw page, charge every candidate before label/visibility
filtering, and retain the existing global maxEdges overflow error and deterministic
BFS/output order. Project edge attributes under the graph's persisted field policy.
Bound incremental standalone vertices/edges before retention, then measure the
exact complete GraphTraversal. Cycles/self-loops terminate as before.

Worker owns only Traverse in `GraphAndSeries.cs`, new
`Core/Features/GraphTraversal/` helpers and new `UnitTests/Features/GraphTraversal/`
tests. Lead's same-file TimeSeries/vector joins are complete before worker start;
lead will not edit that file concurrently. Public signature/defaults, mutations
and all other Core/shared files stay. Lead owns docs/server integration.

Tests first: real converging/repeated large visible and hidden target rows must
succeed under a byte cap permitting one target lookup but not repeated payload
reads; hidden downstream paths remain excluded. Cover cycle/label/global edge
count, exact envelope/output overflow, cancellation/unchanged position and a healthy
following call. Preserve existing assertions; no doubles/local test run. GitHub
TUnit then RF3 SDK/MCP and node resource artifacts own qualification.

## Accepted KL-023 independent reachability oracle stage, 2026-10-05

REQ-GRAPH-006 / AC-GRAPH-005: compare the current directed traversal with an
independent test-owned graph model backed by genuine `TestDatabase`/ZoneTree
fixtures. Compute minimum hop distances independently from the production
reader, native keys and returned result; compare complete vertex/edge membership
and ordinal public order for directed cycles, converging equal-depth paths,
shorter direct paths, shuffled insertion and hidden intermediate vertices.
For each requested depth, expand only visible permitted targets and prove that
vertices beyond that depth are absent. Depth is a successful bounded query
boundary; a shallower depth must not be reclassified as a budget error.
An exact distinct-visible-vertex or examined-edge cap succeeds, while one less
than the required count returns `BudgetExceeded` without a partial result.
Count outgoing candidates before label/visibility filtering. Preserve the
existing cancellation/deadline and healthy-follow-up cases.

TASK-KL023-ORACLE owns only
`tests/KeyLoad.UnitTests/Features/GraphTraversal/Assertions/GraphTraversalReferenceBfsOracle.cs`
and `Cases/GraphTraversalReferenceOracleTests.cs`. Root freezes this contract,
reviews and joins the private packet, and runs Aspire normal/scalar followed by
exact-source Linux and real SDK/official MCP RF3 qualification. Existing
ADR-004/010/014 define the unchanged read, budget and persisted authorization
boundaries; no additional architecture decision is needed for this test-only
stage. It adds no runtime/data/public contract or dependency; rollback removes
the unused test files. Public shortest-path results, batched frontier execution
and the complete KL-023 acceptance remain open.

## Current source behavior

`UpsertEdge` validates identifiers and graph resource, requires both endpoints in the same atomic partition, verifies both referenced documents are visible, checks graph field-write policy and expected revision, then updates the edge plus outgoing/incoming adjacency records in the command transaction. Deleting an edge removes both adjacency entries and its canonical edge record. Cross-partition edge writes currently fail with `UnsupportedCapability`.

`Traverse` starts from a visible vertex and performs directed outgoing breadth-first traversal. Optional labels filter edges; hidden/missing target vertices are not returned or expanded. A visited-vertex set terminates cycles. The current hard limits are max depth 16, max vertices 10,000 and max edges 50,000; request defaults are depth 3, 1,000 vertices, and 5,000 edges. Shared work/deadline/read/result budgets can reject rather than return an unmarked partial traversal. Returned edge attributes are projected using persisted field policies; output order is deterministic by vertex and edge identifiers.

The source does not establish incoming traversal, shortest-path results, cross-partition edges, async reverse-adjacency repair, ranked graph retrieval, or a partial-result opt-in. Product-design suggested defaults and future algorithms are not current guarantees.

## Requirements and acceptance

| Requirement | Measurable acceptance | Existing TUnit evidence or planned test |
|---|---|---|
| REQ-GRAPH-001: keep edge and adjacency mutations atomic and partition-scoped | AC-GRAPH-001 passes when create/update/delete leaves edge and both adjacency directions consistent; stale revision, hidden/missing endpoint, or endpoint in another partition rejects without partial writes. | Planned `GraphEdgeMutationRevisionAndPartitionCases`; current transaction rollback tests establish shared mutation atomicity but do not constitute a full edge index matrix. |
| REQ-GRAPH-002: traverse only authorized visible graph/document data | AC-GRAPH-002 passes when a hidden/missing intermediate vertex and its downstream path are excluded, denied graph access fails, and disallowed edge attributes are projected. | `TraversalStopsAtHiddenIntermediateVerticesAndHandlesCycles`, `FilteredOrHiddenGraphEdgesCountAgainstTheGlobalVisitBudget`. |
| REQ-GRAPH-003: bound traversal depth, visits, time, and result | AC-GRAPH-003 passes when cycles terminate, each invalid/exhausted bound returns a visible budget error without an implicit partial result, and cancellation/deadline is honored. | `FilteredOrHiddenGraphEdgesCountAgainstTheGlobalVisitBudget`, `SearchAndGraphResultBudgetsIncludeProtocolMetadata`, `QuerySearchAndGraphRequestsShareOneWorkingSetReservationBudget`; planned adversarial max-depth/max-vertex/max-edge boundary matrix. |
| REQ-GRAPH-004: keep graph traversal stable under shared read accounting | AC-GRAPH-004 passes when point reads, adjacency scans, edge/vertex dereferences, and response metadata are charged to the operation budget and a subsequent healthy operation remains usable after rejection/cancellation. | `GraphEdgeAndVertexDereferencesConsumeTheReadByteBudget`, `OneReadDeadlineCoversSubsequentPointAndScanWork`, `CancelledReadsStopBeforeTouchingCanonicalState`. |

## Flows and failure behavior

Positive: an authorized edge write binds both visible document endpoints to the same partition and stores edge/adjacency state atomically; traversal expands only allowed outgoing edges within supplied limits. Negative: foreign partition endpoint, stale revision, denied resource/row/field access, or malformed identifiers reject. Edge: self-loops and cycles terminate through visited tracking; hidden targets do not expose their outgoing neighborhood; an exact limit boundary succeeds only if no additional work exceeds it. Exhaustion is an explicit budget error, not a silently truncated result.

## Decisions and verification

Related decisions: [ADR-001](../ADR/ADR-001-partition-identity-affinity.md), [ADR-004](../ADR/ADR-004-committed-read-views.md), [ADR-005](../ADR/ADR-005-canonical-keyspace-codec.md), [ADR-010](../ADR/ADR-010-query-budgets-security.md), [ADR-014](../ADR/ADR-014-principals-rbac-row-policy.md), and [ADR-016](../ADR/ADR-016-atomic-physical-placement.md). Cross-partition physical placement and movement follow [ADR-017](../ADR/ADR-017-ownership-session-tokens.md); they do not change the current same-partition write contract.

```mermaid
flowchart LR
    Request[Authorized bounded traversal] --> Start[Check visible start vertex]
    Start --> Frontier[Directed outgoing adjacency frontier]
    Frontier --> Edge[Read edge and enforce label and policy]
    Edge --> Target[Check visible target vertex]
    Target --> Budget[Charge depth vertex edge time and bytes]
    Budget --> Result[Deterministically ordered bounded result]
```

The listed TUnit names are source mappings only; this authoring task ran no product tests. GitHub Actions owns build/TUnit/recovery/Aspire RF3 qualification through .NET SDK and official MCP clients; required official MCP parity is pending. Cross-partition traversal, shortest paths, graph-ranking roles, external graph providers, and production-readiness claims remain planned and require their own measurable contracts.


## KL-022 deterministic storage mutation reference stage

TASK-GRAPH-STORAGE-REFERENCE-001 implements existing REQ-GRAPH-001 / AC-GRAPH-001 and the original KL-022 random graph reference, local mutation atomicity and duplicate-command criteria. This adds test acceptance coverage only; production/API/storage/revision contracts do not change. ADR: no new boundary; existing ADR-016/057 atomic commands and ADR-102 monotonic graph owner revisions remain authoritative.

Before implementation, freeze two real ZoneTree whole-operation regressions: GraphStorageMutationReferenceTests executes seeded deterministic insert/update/rebind/delete/reinsert sequences against independent test-owned literal edge state, compares all outgoing and incoming edges including revisions/attributes after each operation, retries each exact command without new effects, rejects changed payload for the same command identity, closes/reopens actual storage, compares persisted reference state and completes another healthy mutation. GraphStorageAtomicEffectsTests commits entity/edge/event/queue together, retries the complete batch, then places effects before a stale-edge failure and proves every model unchanged/absent followed by a healthy operation. Missing endpoint and stale expected revision are real command rejections, not injected provider faults.

Ownership: tests/KeyLoad.UnitTests/Features/GraphTraversal/Cases/GraphStorageMutationReferenceTests.cs and GraphStorageAtomicEffectsTests.cs, cohesive feature-local Fixtures/GraphStorageReferenceFixture.cs and Assertions/GraphStorageReferenceAssertions.cs. Root guards/joins docs and tests, compiles and executes normal/scalar native TUnit plus required recovery/RF3 gates. Authored tests and source review alone do not close KL-022; native original reports and real client qualification remain required. Existing KL-023 traversal oracle and its independent budgets remain unchanged. Rollback removes these test/docs additions only; no migration or production format change.


## KL-022 persisted rejection and exact retry correction

TASK-GRAPH-REJECTED-OUTCOME-002 corrects the test oracle for REQ-GRAPH-001 / AC-GRAPH-001 under existing ADR-016/057/102; production behavior and acceptance thresholds do not change. The original Stage VIII normal/scalar reports retain the stale Conflict and unchanged-position expectations as actual failures.

Freeze before implementation: a valid fresh batch whose execution rejects a stale edge revision returns exactly RevisionConflict with “The expected revision does not match.”; a missing endpoint returns exactly NotFound with “The graph vertex is unavailable.”. DatabaseEngine.Apply resets staged model changes and persists the failed command outcome, so its first rejection advances the native store position by exactly one. Repeating the identical CommandId/payload returns the complete identical OperationResult and identical persisted native outcome bytes without advancing that post-rejection position. Changed content under an existing CommandId remains the distinct Conflict contract covered by GraphStorageMutationReferenceTests. Pre-admission/decode failures are outside this execution-rejection assertion.

GraphStorageAtomicEffectsTests and feature-local Assertions/GraphStorageRejectedOutcomeAssertions.cs must preserve complete existing document, stream identity/head/events/HasMore, queue and outbox results; the current stream read cut must equal the exact post-rejection store position, prove staged entities/events/messages absent and compare the full independent canonical/incoming/outgoing graph oracle after rejection/retry. Both cases finish with an actual healthy mutation. Root owns fresh compilation and original native normal/scalar execution; this correction is authored and source-reviewed only until those reports exist. It does not close RF3, recovery, endurance or global qualification gates.


## KL-023 elapsed deadline whole-operation proof

TASK-KL023-ELAPSED-DEADLINE supplements original architecture KL-023 time-cap acceptance under REQ-GRAPH-003/004, AC-GRAPH-003/004 and existing TimeProvider REQ/AC-TIME-001/002 contract. Scope: two real TestDatabase/ZoneTree DatabaseEngine.Traverse whole operations. Existing native clock injection and typed QueryDeadlineSeconds are reused; only permitted clock double, no provider/database/transport doubles. No production/seam/config/format changes and no new ADR: existing bounded graph/clock contracts suffice.
AC: configure collection+graph and atomically commit three literal documents and directed cycle; healthy traversal returns exact literal ordered qualified vertex and edge memberships. Capture full bounded actual native logical record snapshot and acknowledged position. Arm monotonic clock only after actual native adjacency record threshold2 or3; execute Traverse and require exact BudgetExceeded/message, actual elapsed trigger and native range progress, no partially returned GraphTraversal. Disable trigger, require unchanged full persisted record keys/bytes and position, then same engine must return byte-identical complete healthy traversal. All assertions are operation/state; native diagnostics synchronize permitted clock only and do not establish behavior by themselves. Root owns native build/unit/scalar/final gates and traceability/selector/coverage joins. No execution in private packet.


### TASK-KL022-KL023-ORIGINAL-ACCEPTANCE-CLOSEOUT-001

Original architecture KL022/KL023 acceptance is qualified narrowly by authenticated Linux run37666943488, attempt1, source `4e18ba1ba29ae31970302e6bfa43ad9c04ead7e0`. The original full normal and scalar TRXs each pass these eight operations (three KL022 and five KL023 instances). This does not close the broad GraphTraversal feature, cross-partition delivery, RF3/endurance/performance, code coverage or changed StageXI compiled cohort. Original unrelated failed cases remain preserved.

KL022: `Kl022SeededMutationsAndRetriesMatchIndependentAdjacencyStateBeforeAndAfterNativeReopen` compares independently maintained adjacency/revisions after seeded create/update/delete, immutable-ID replay and conflicting payload, then actual ZoneTree dispose/reopen and healthy edge. `Kl022EntityEdgeEventQueueAtomicCommitRetryAndLateEdgeConflictLeaveExactModelState` proves one complete mixed-model commit, stable same-ID retry, exact RevisionConflict stored rejection/replay and unchanged rejected entity/event/queue/graph effects. `Kl022MissingEndpointRejectsTheWholeEntityEdgeBatchAndFollowingMutationSucceeds` proves exact NotFound failure receipt/replay, no partial entity/edge effects and healthy following mutation.

KL023: `TraversalMatchesIndependentReferenceAtEachDepthAndKeepsOrdinalEntityOrder` checks cycles, independent BFS frontiers and literal depth boundaries; `ExactDistinctVertexAndExaminedEdgeCapsSucceedAndOneLessRejects` checks inclusive exact node/edge bounds and exact BudgetExceeded one-less. `AcGraph006ShortestPathMatchesIndependentBfsForTiesCyclesAndFullEntityRefs` checks independent shortest paths with ties/self/cycles/full references. `ElapsedCapDuringNativeAdjacencyWorkRejectsWithoutPartialPageAndHealthyTraversalFollows(2)` and `(3)` advance the owning clock only after actual native adjacency work, require exact deadline error/no partial page/full-store equality and healthy literal continuation. No sleep or timing enlargement.

Original compilation binding: 515 original source witnesses across the UnitTests graph fixture/helpers and complete Abstractions/Core/Security/Storage.IO/Storage.ZoneTree modules match source Git SHA, portable-PDB native document checksums and complete same-assembly compile receipts. Every central build/project/package input matches the same original SHA. Original UnitTests DLL SHA `30466403c2e80c5a77f616f71d88f4654198d6a9abefc08915c995bc3035cc8c`, PDB SHA `226a6ed99d079308c0bc986279a73c02dde670ff339f3e9b99af639e1be534b8`, MVID `6aa5ad29-ba69-4fe5-8a22-ef3e923b6ff7`. Normal TRX SHA `725e41e17c55e3df4209650d5ce2e8842ab15c8b1748f0348e12a7aaabadd092`; scalar TRX SHA `b35fe02f99453a80313af6f8123099a2d9c0274b7fc70fee7ff4f1dcff2880da`. Exact original module identities:

- KeyLoad.UnitTests: DLL `30466403c2e80c5a77f616f71d88f4654198d6a9abefc08915c995bc3035cc8c`, PDB `226a6ed99d079308c0bc986279a73c02dde670ff339f3e9b99af639e1be534b8`, MVID `6aa5ad29-ba69-4fe5-8a22-ef3e923b6ff7`.
- KeyLoad.Abstractions: DLL `dbdeae79ed6e4ff1f6d5a3866a3e0646e4d2111db13fbe9b7aa882ec87ec50d6`, PDB `255c2641f343d475e981f743bdde84cca02c75b92ddcf5c5a105339a79c88301`, MVID `bd7f2370-981f-4cfa-a229-f715a6cab546`.
- KeyLoad.Core: DLL `ab4aa13e10557bce17912be67012640e0815b106da8ae7310a5214a718c55d81`, PDB `6213d51c3c4a90573dcb4a7977807d012d4b2a81ec2a0f19e804c1f97eedf314`, MVID `3ae0a3b1-6ab5-41f0-8653-642316cd636f`.
- KeyLoad.Security: DLL `774601b8b595bc4d5af03605b6eba285317a17ca8a3c854018e073ade2345e7c`, PDB `69e068448d9ead25f1d348c0074cb4c9b6ff2a32d285deed0dbc68e4cc3e3cf0`, MVID `b946ab33-0c63-415f-ab22-f824fdcfacd6`.
- KeyLoad.Storage.IO: DLL `4ca43435e641d544a41888711cd212cb93c8246790faa9e84e8dd758358eb1ab`, PDB `bcbb7ad25fb89781857ff072b53781e5998940e70fe24685fa5fa37bfb0e9049`, MVID `1076ce61-07f3-4379-82a0-470c8d88200b`.
- KeyLoad.Storage.ZoneTree: DLL `ab6b103e385b6f9ba7d932eac82a871576a437a988a36473dc6754aa94e4ab63`, PDB `740cc621422be6c9a5906ce2de660e765e3daa140cb7cd3dfac811b09e3e0564`, MVID `bf86999f-6550-488e-926c-f28636b89d28`.
