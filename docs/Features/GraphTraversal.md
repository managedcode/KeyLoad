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

## TASK-GRAPH-SHORTEST-PATH-OBSERVED-CANCELLATION-002

REQ-GRAPH-003/004, AC-GRAPH-003/004 retain original cancellation semantics. Original
aa103 Linux normal failure returned a complete path before the independent polling
observer requested cancellation; scalar passed. Replace exclusive polling/thread
helpers with an owning operation TimeProvider which synchronously cancels the original
token only after both charged bytes exceed the actual empty-adjacency probe and native
same-store RangeExaminedBytes increases. Native storage/read-cut and budget are real.
Require exact original-token cancellation, no partial result, full storage bytes/cut
unchanged, then the identical busy-source request returns the complete literal no-path
result and identical store/cut. No corpus/budget/timeout increase or permitted-success
branch. No worker/controller remains to settle; synchronous native operation stack
settles before healthy continuation and all unexpected/fatal failures propagate.
Authored source requires original native normal/scalar and Linux reruns; preserve
original failure receipt.

R2 full-image correction: unchanged4096edge corpus exceeds one4096record page. A feature-owned native Scan helper uses unchanged4096record pages, at most16pages under one Store.Read cut, strictly increasing original native keys as exclusive afterKey, no empty continuation and final HasMorefalse required. Every page is synchronous/joined before image returns. Full bytes are compared without dropping records; no query quota/deadline/corpus change.

## KL-065 incoming projection negative to cold healthy flow, 2026-10-10

REQ/AC-GRAPH-002/004 and REQ/AC-AUTH-005/007 map to the unchanged HiddenSourceEndpointIsOmittedFromIncomingProjection and HiddenTargetEndpointIsNotFoundWithoutReturningProjectionRows. The latter original source has no owner: genuine healthy continuation repairs its owner as well as the hidden target, rather than trusting a caller role. Full literal edge, delivered revision, projection and call-current cut follow row/grant repair; grant refusal and revocation preserve the full bounded canonical image. Same-root native close/open preserves node/incarnation and original outcomes; original row-repair command replay returns each complete receipt without a second document revision. Original declarations, limits and initial assertions remain.

The source-proven embedded same-ID repair replay also retains the complete bounded native key/value image and physical cut: the cached path reauthorizes and returns before clock/effects, with no positive replicated apply index. Lexical cold-store ownership retains both the initiating operation failure and any distinct real disposal failure, emitted after cleanup.

## TASK-GRAPH-TRAVERSAL-ADJACENCY-002 — canonical outgoing identity before filtering

The native traversal reader decodes the outgoing index value and reads an edge,
but does not compare the complete visited key, edge ID and From entity or validate
the persisted edge fields before filtering/projection. This can return a
disconnected edge or hide malformed canonical data behind a label filter. Reuse
the existing native graph edge validator and require the exact outgoing
key/value/source relationship before any label/visibility filter. Candidate
charging, bounded work, deterministic ordering and current authority remain.

REQ-GRAPH-ADJACENCY-001 maps to AC-GRAPH-ADJACENCY-001/002: every examined outgoing
record must reference its canonical edge under the same graph/source partition,
with the complete current source EntityRef, valid native identifiers, positive
revision and canonical bounded attributes. Missing/invalid rows return Corruption
without a partial result or changed full native image/cut, including when a label
filter would otherwise exclude them. Independent whole-operation tests seed a
healthy first candidate and damage only the second candidate; repeat the original
request after a joined cold open, repair only the owned row, replay the original
seed receipt, then perform fresh graph work and a second cold.

REQ-GRAPH-ADJACENCY-002 maps to AC-GRAPH-ADJACENCY-003: preserve valid canonical
source edges which reference another same-owner atomic partition (ADR-102).
Those visible endpoints remain full EntityRef results; they are not expanded as
frontiers in this partition-scoped traversal. A foreign vertex whose textual
collection/ID collides with a local vertex must not expand the local vertex's
edges or inherit another partition's graph authority. This does not introduce
cross-partition traversal or alter reverse delivery/placement contracts.

Source map: Core GraphTraversal Queries/GraphTraversalReader and existing
Validation/GraphCrossPartitionValidation; Unit GraphTraversal new Cases/Fixtures/
Helpers/Assertions/Models integrity flows. Existing ADR-004/005/010/014/102 define
read-cut, native-key, budget, authority and cross-reference boundaries; no new
public API, format, dependency or physical ownership is added. Source-only until
original native build/analyzer/formatter and TUnit results; exact-source Linux,
process/RF3 SDK/MCP and resource/performance qualification remain separate gates.
Rollback removes the new guards and additive tests/map together.

## TASK-GRAPH-PATH-CANONICAL-COLD-003 — shortest path canonical edge integrity

Private next-batch refinement of existing REQ/AC-GRAPH-001/003/004/008 and ADR-004/005/010/014/102. EdgeRecord positive revision and bounded canonical JSON are existing authoritative mutation contracts. A native shortest-path candidate must validate those fields before labels and visibility as traversal/incoming do; native inspection is not an automatic repair. No public shape, persistence format, labels/budget/error policy or partition frontier changes. Existing GraphShortestPathValidation currently validates selected adjacency identity and endpoint identifiers but omits revision and attributes canonicality; this is a source-confirmed missing guard, runtime confirmation pending.

REQ-GRAPH-PATH-INTEGRITY-001 maps to AC-GRAPH-PATH-INTEGRITY-001: actual native seed with healthy first candidate and damaged second canonical edge, noncanonical attributes/nonpositive revision × filtered/unfiltered reads, Corruption without partial result or complete image/cut change. Repeated actual refusal across joined same-root cold, only original owned edge-row bytes restored, same request healthy with independently constructed full path/cut, seed receipt immutable replay, distinct healthy graph command and path, second cold complete image/cut/path/receipt stability. Original native baseline failures and repaired runtime are mandatory; source review alone does not close the criterion. Production owners: existing GraphShortestPathValidation, GraphShortestPathReader, GraphShortestPathResultProjector; additive Unit Cases/Assertions only using existing native fixture/input oracles. Root owns private packet and later serialized integration. Linux/RF3/resource/performance gates remain open.


## TASK-GRAPH-PATH-SELECTED-IDENTITY-COLD-004 — selected path identity and native value refusals

This finite KL-023 acceptance extension implements existing REQ-GRAPH-003/004 and AC-GRAPH-003/004, [ShortestPath](GraphTraversal/ShortestPath.md) REQ-GRAPH-008/009 and AC-GRAPH-007/008, and REQ-GRAPH-PATH-INTEGRITY-001 / AC-GRAPH-PATH-INTEGRITY-001 under ADR-004/005/010/014/098/102. The preceding positive-revision and canonical-attributes correction remains separate. Native adjacency and selected canonical edge identities must be validated before label filtering; no public shape, storage format, error, budget, partition, readiness or timeout contract changes. No new ADR is required because these are existing bounded path and canonical graph contracts.

REQ-GRAPH-PATH-IDENTITY-001 maps to AC-GRAPH-PATH-IDENTITY-001: nine native selected-row contradictions (missing canonical edge, foreign edge identity, foreign source, adjacency value pointing to the genuine healthy edge under the damaged edge key, empty label, empty target identity, null target, malformed adjacency bytes and wrong native adjacency type) each run through the real shortest-path operation both with and without an excluding label filter. Every operation has an actual valid first adjacency candidate, the selected second damaged candidate, exact `Corruption`, no partial path and unchanged full bounded canonical image and read cut. Repeat the same refusal, join same-root cold reopen and repeat it again; restore only the captured original owned row; require the exact original healthy complete image at one repair commit later. Retry the unchanged request with complete independently constructed path fields, replay the original complete seed receipt without a second effect, execute a new genuine graph command and read its complete path, replay that command, then join a second cold reopen and require exact image/cut, original and fresh path results and both complete receipt replays. Reads and replays retain their original effect-free checks.

Canonical slice map: Unit `Features/GraphTraversal/Cases/GraphShortestPathSelectedIdentityWholeFlowTests.cs` owns the eighteen native declarations; `Helpers/GraphShortestPathSelectedIdentityFlow.cs` owns the real operation lifecycle and reuses the existing fixture, literal path assertions, native fault-row ownership and complete image oracles. Core ownership remains the existing GraphShortestPathReader/GraphShortestPathValidation; there is no production correction in this acceptance-only packet. SDK/MCP and RF3 surfaces are unchanged; this local genuine-store flow is not their qualification. Original declarations, compiled image identities and native reports must be retained before claiming execution. Current status: authored private development packet, not executed or admitted to current-main qualification. Exact-source Linux, required process-recovery, Docker/Aspire RF3, endurance and measured performance gates remain open. Rollback removes only these two additive test files and this appendix.


## TASK-GRAPH-VERTEX-CANONICAL-AUTHORITY-005 — selected vertex identity and complete graph operations

This refinement implements original KL-022/KL-023 REQ/AC-GRAPH-001/002/004 and the canonical EntityRef contract of DatabaseComposition REQ/AC-002 under existing ADR-004/005/010/014/016/057/102. A decoded DocumentRecord selected by the graph vertex's canonical document key must have exactly that EntityRef before it becomes write endpoint or traversal visibility authority. Typed composition already enforces this identity; the ordinary graph paths currently omit it. A mismatching selected record returns Corruption, not a substituted vertex, partial traversal or successful edge mutation. Current authorization, row visibility leases, read budgets, missing/deleted/hidden errors, storage wire shape, public API and physical-owner boundaries remain unchanged. This repairs existing canonical authority rather than adding a new relational constraint or SQL dialect rule.

The native whole-operation gate selects a genuine valid start or a genuine downstream vertex, captures its original owned serialized row, and persists the same valid DocumentRecord type with a different Reference under the selected key. Real traversal and shortest-path operations must refuse with Corruption without changing any canonical byte or read cut. Mixed PutDocument plus UpsertEdge and PutDocument plus DeleteEdge must refuse atomically: Corruption is thrown through the existing atomic boundary and does not create a scoped outcome, commit or domain effect. The original native exceptions are retained, and each unchanged GUID re-refuses while every canonical row and position remains byte-identical. A joined same-root cold reopen preserves the entire corrupt image and repeats both original typed refusals; exact original row-byte repair changes only that row and one commit, restores independently constructed complete graph/path results, and restores the unchanged original Upsert GUID to genuine success because corruption never persisted a poisoned receipt. Complete independent witness/edge mutation receipts and revision2 are required; original seed receipt replay, a distinct genuine healthy graph mutation, both full successful receipt replays, and a second joined cold reopen preserve full canonical image/cut/results. The immutable source-preview correction retains the original erroneous stored-Corruption assumption as history; no production failure contract changes.

The genuine Aspire-owned Docker RF3 public gate uses the .NET SDK and official MCP SDK against discovered endpoints. A single-partition document-plus-edge command creates exact canonical endpoints, then rebinds the edge, deletes it and reinserts at monotonic revisions 1/2/3/4. Caller-visible complete outgoing traversal and incoming rows prove both adjacency directions; changed same-GUID content, a missing endpoint after a staged document, and a stale edge revision refuse without the staged document or changed graph. Every original successful receipt is compared in full across both callers before and after two actual three-node cold restarts, then a fresh healthy edge succeeds and remains readable. These public logical checks do not claim raw no-effects from public reads, and the public API does not manufacture storage corruption. The native operation gate owns the byte-preservation proof; RF3 corruption injection is not claimed by this flow.

Owning paths: Core GraphVertexReads and GraphVertexVisibility; additive Unit GraphVertexAuthorityWholeFlowTests and its Fault/Assertions/Flow roles; additive Integration GraphVertexPublicWholeFlowTests and its State/Assertions/Commands/Flow roles. Existing case identities and arguments remain intact. Classification: functional feature acceptance, native UID unknown until actual discovery. Current status: N-only authored regression and product correction, not compiled, executed or qualified. Exact-source Linux native Unit/recovery/RF3, fault/endurance, measured resource/performance and coverage gates remain open. Rollback removes only the two guard additions, additive roles and this appendix.
