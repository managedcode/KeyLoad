# Jepsen ParticleDB review and KeyLoad acceptance

Reviewed 2026-11-01 against [Jepsen's ParticleDB 1.1.4 analysis](https://jepsen.io/analyses/particledb-1.1.4). This is a source-informed review of KeyLoad work, not an independent Jepsen evaluation of KeyLoad.

Related execution choices and their original source links are in the [competitor performance review](competitor-performance-review.md).

## What the report establishes

Jepsen evaluated ParticleDB 1.1.4 through 1.1.18.11.rc-22 on single nodes. Most reported issues required neither injected faults nor multiple clients. The report separates predicate/type errors, transaction anomalies, acknowledged-write loss and recovery failures. Its generated comparison workload checked supported SQL behavior against PostgreSQL and reduced failures to smaller reproductions. It also tested abrupt termination, loss of unsynced writes and damaged storage separately. Vendor claims about fixes in later releases were not independent retests by Jepsen.

The relevant lesson for this change is to prove complete observable operations and original failure behavior before attributing success to an optimizer, isolation label, process restart or passing test count.

## Application to the current changes

| KeyLoad boundary | Required observable behavior | Current implementation/test work |
| --- | --- | --- |
| Physical query selection | An index supplies candidates; the complete predicate still decides results. Constants, reversed operands, OR and NULL cannot turn into unrelated primary-key lookups. | `AccessPathPredicateSemanticsTests` exercises literal expected rows through real SQL and AST operations, including `4 = 5`, index/point residuals, zero and NULL. Existing equality/range tests retain duplicates, contradictions, field denial, cursors and cancellation. |
| EXPLAIN | The described access path is the path consumed by execution. Planning does not scan candidate records or expose document payloads. | `QueryAccessPlanner` is shared by scalar planning and execution. `QueryExecutionPlanTests` checks index/range/full-scan choices, no range visits, real read cuts, authorization and healthy subsequent execution. This is not a cost-based optimizer or `EXPLAIN ANALYZE`; no estimated or actual execution counts are invented. |
| Storage routing | One immutable native snapshot may reuse its own resource-mode decision. Different resources, native writes and malformed keys must retain their original semantics. | A single routing decision lives only in the native read operation. Writable operations continue to resolve their catalog state. Complete encoded-key validation remains mandatory. Literal malformed key cases and real plain/atomic storage tests cover these boundaries. No record cache, write gate or additional storage authority is introduced. |
| Client identity and permissions | Authentication establishes the subject. Current persisted policy still controls each database effect; metadata-consuming adapters require fresh authority metadata. | Standard ASP.NET endpoint policies distinguish operation-owner authorization from adapters consuming permissions. Connection concurrency, identity isolation, revocation, cancellation, deduplication and joined cleanup remain required tests. The credential's user grain still owns credential invalidation and expiry. |
| Exact vector selection | Bounded selection must preserve final ordering and reciprocal-rank scores. Graph filtering and hybrid/global fusion must not lose candidates. | Only a terminal vector-only branch with a strictly decreasing fusion score across the K boundary may retain K. Complete rankings remain available to hybrid, graph and distributed consumers. Tests cover ordinal ties, allowed IDs, zero/subnormal weights and projected revisions. |
| Graph aggregation | Counting must preserve the same traversal, visibility, depth, uniqueness and work limits while omitting unused output arrays. | `GraphTraversalProjectionTests` exercise cycles, parallel edges, hidden intermediates, fresh revocation, all projection modes, cancellation, bounds, serializers and healthy follow-up. The shared benchmark scenario/dataset is unchanged; its driver requests the appropriate server projection. |

## Release and measurement boundaries

The local checks above do not establish distributed serializability, power-loss durability or absence of defects. Complete solution, native operations, process recovery, real cluster SDK/MCP, coverage and fault/endurance qualification retain their existing distinct gates. The canonical Release workflow publishes the exact commit only after Build and Tests succeeds. Benchmarks must then resolve that published image and retain the original measured results and errors.

Local profiling uses separate clean timings and instrumented windows with retained binary/process identities. A short vector allocation window that captured mostly idle time is excluded from active-work attribution. The longer active window attributes substantial allocation work to our storage-routing adapter; that is a KeyLoad implementation finding, not a confirmed ZoneTree defect.

A complete generated differential suite against PostgreSQL, history checking for transaction anomalies and power-loss/disk-corruption qualification remain separate work. This review does not claim those gates are implemented or passed.

## Execution ownership decision

The candidate change that executed synchronous capabilities directly inside `ConnectionGrain` was removed during review. `ConnectionSynchronousExecutionTests.SynchronousOperationDoesNotBlockAnotherReadOnTheSameConnectionAndPhysicalOwner` holds the real execution thread and requires another operation on the same connection and physical owner to finish independently. Orleans interleaving does not preempt a synchronous turn. The existing independent request execution activations therefore remain. Permission-metadata round trips and per-record storage-routing work are optimized without weakening that parallelism requirement or adding a custom scheduler.
