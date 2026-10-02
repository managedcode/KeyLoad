# Memory and performance review

Goal: review the complete current KeyLoad operation path and fix every evidenced
memory, excess-read and performance defect. Success requires real correctness,
bounded resource behavior and measured GitHub qualification; a narrow patch or an
unmeasured claim of speed does not satisfy the owner's objective.

## Current evidence and constraints

- Current checkout is dirty at 9c570f8c33a7a9667507a8e1c0ca68860de3be45. A new exact
  worktree/index baseline is in /private/tmp/keyload-performance-baseline.
- Earlier bounded read/search changes and Orleans migration are present locally;
  inspect current sources rather than treating them as completed or disposable.
- Strict quality gates previously reported 1180 errors and formatter failure;
  revalidate them and resolve required qualification prerequisites without
  disabling rules, weakening tests or altering unrelated changes.
- RF3, node-local storage ownership, ordered atomic apply, durable outcomes,
  persisted authorization, cancellation and independent request grains remain
  mandatory. Orleans skill applies to routing/lifecycle/transport changes.
- Development builds/static checks are local; all tests, process recovery,
  Docker/Aspire RF3 and benchmarks execute only through GitHub Actions.

## Options and recommended direction

1. Tune limits only: insufficient because repeated scans, copies, materialization
   and retained objects can remain. Reject as a substitute for repairing ownership.
2. Add broad caches: risk stale reads, security leaks and unbounded retained memory.
   Consider only where measured need and invalidation/ownership are explicit.
3. Audit by operation and resource lifetime, fix algorithm/read shape and bound
   allocations at the owning boundary, then verify public results and fault paths.
   Recommended; retain exact ranking/serialization/consistency unless a documented
   acceptance contract explicitly changes them.

## Read-only research graph before implementation planning

| Task | Requirement / provisional acceptance | Owner / model | Permissions / ownership | Dependency / output / join |
|---|---|---|---|---|
| TASK-MP-001 | REQ-MP-001 complete review; AC-MP-001 real located findings | Lead / strongest planner | Shared plans, docs, config integration only | Capture baseline; join all reviewed inventories before write delegation |
| TASK-MP-002 | REQ-MP-002 bounded reads; AC-MP-002 caller results preserved | Research worker / capable high reasoning | Read-only Core, Query, Storage.ZoneTree and related tests | Inspect real scan/copy/query paths; located findings, severity, fixes and realistic tests |
| TASK-MP-003 | REQ-MP-003 bounded lifetime; AC-MP-003 RF3/recovery intact | Research worker / capable high reasoning | Read-only Replication, Orleans, Server and related tests | Inspect retained logs, snapshots, transport/admission, grain lifetimes; invariants and fixes |
| TASK-MP-004 | REQ-MP-004 transport and proof; AC-MP-004 real operation/perf evidence | Research worker / capable high reasoning | Read-only Client, Artifacts, AppHost, benchmarks, workflow | Inspect payload/copy/disposal paths and usable CI proof; findings and measurements |

Research can run in parallel with disjoint responsibilities. No worker may edit,
commit, push, install tools or run local tests/benchmarks. Each must read the nearest
AGENTS, report complete/blocked/failed with exact files/lines and avoid speculative
optimization claims. Lead owns shared contracts, plans and all join decisions.
Write delegation begins only after detailed acceptance, plan and required feature
and ADR implementation contracts exist and are accepted under owner authorization.

## Risks and open evidence

Located follow-ups use one DatabaseEngine-owned analytical reservation count
rather than per-engine semaphores (MP-008). A gate-scoped budgeted read-view
adapter charges existing principal/resource helpers without duplicating policy;
borrowed source records and complete cursor-envelope counting address MP-034.
The adapter cannot escape its store action or mutate a transaction. Source
generation, cursor/policy authority and missing-history errors remain explicit.

Strict contract prerequisites need an explicit CLR API migration. Prefer
ImmutableArray for ordered DTOs/vectors and ReadOnlyMemory for base64 buffers over
IReadOnlyList everywhere, which would lose vector spans and retain mutable backing
lists. Freeze once at boundaries, preserve wire/hash bytes, rename owned storage
Get once and join every consumer; ADR-041 records the decision and rollback.

Raw-byte budgets do not prove bounded heap/RSS. Storage iterators and transaction
overlays may allocate before query accounting; repeated canonical dereferences,
whole-snapshot reads and HTTP buffers must be inspected. Read gates, ranking,
authorization and idempotency cannot be traded away for throughput. CI measurement
must separate operations/allocations/GC/working set and cold/warm data effects;
comparison topology and limits must be visible. Missing runtime coverage is a
tracked task, not evidence that a defect is absent.
