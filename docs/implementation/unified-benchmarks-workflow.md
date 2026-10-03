# Shared parallel Benchmarks delivery

Source changes implement the owner corrections recorded by ADR-064. This record
distinguishes configuration/source checks from actual GitHub qualification.
The root brainstorm/acceptance/working plan are task-local ignored files; the
durable delivery contract and evidence are retained here.

# Acceptance

Goal: Benchmarks shows TimeSeries within common image preparation and the complete
performance cohort, with no separate TimeSeries branch. Existing owner direction
authorizes this repair and scoped main delivery. Actors: trusted own-main push or
manual workflow, read-only native preparation/measurement and Pages-only deployment.

- AC-UB-001: No TimeSeries-only workflow job. The existing pinned Docker-image test
  runs during common comparison-images preparation and retains real image facts on
  success/failure. A missing or failed test blocks dependent measurement/publication.
- AC-UB-002: The canonical 270 cells still include 9 targets, native node counts
  1/2/3, four CRUD and six vector/queue/graph/stream scenarios; 27 preflights
  and isolated Linux runners remain. No workload, correctness or resource gate is
  omitted or relabelled. Each matrix stays within the provider's 256-cell limit.
  Preserve TimeSeries workload/model/image tests in common preparation; its pending
  intensive native6/30-cell lifecycle/aggregation remains explicitly unqualified.
- AC-UB-003: Website qualification depends on the complete successful aggregate,
  which transitively includes common image qualification; all same-run provenance,
  least privileges, artifact identities and image-facts retention remain.
- AC-UB-004: Scope contains only orchestration/policy/docs/regression changes.
  Preserve unrelated dirty work, no stash/force/protection bypass, no release.
- AC-UB-005: Every job and authored step in CI, Benchmarks, Release and used
  composites has a short concrete readable name. Source-bound displayed-name
  collectors/validators and test oracles change together; YAML IDs, historical
  archived bytes and permissions remain. Fail if a raw command/action hash or
  machine job ID replaces an authored label, or the new real executor is rejected.
- AC-UB-006: Build/checks, planning and image preparation have no mutual dependency;
  preflight, CRUD and specialized matrices depend only on plan and image outputs,
  never on each other and have no arbitrary max-parallel cap. Aggregate explicitly
  waits for build, plan, images and all three matrix jobs. Failure/missing/skipped
  work blocks aggregation/publication; GitHub account capacity may queue jobs.

Tests: TUnit workflow-source regression covers AC-UB-001/003; existing real isolated
plan/native tests plus a source completeness assertion cover AC-UB-002. Actual
GitHub Benchmarks is the integration proof; CI build/rules/full project tests remain
required. AC-UB-004 receives manual scoped-diff/index review. Tests never run locally.
No runtime data/API/serialization migration; rollback reverts scoped source changes,
retains historical evidence and restores prior orchestration without rewriting it.
AC-UB-005 maps to TUnit WorkflowStepNameTests and existing authenticated
image/worker/aggregate/current-job/source-proof cases. Workflow YAML has no
executable numeric collector; no coverage claim. The existing production-script
coverage gates remain mandatory and unchanged.
AC-UB-006 maps to source graph regressions and the actual GitHub jobs dependency
graph. No measurement instances or resources share a runner; only independent
orchestration changes, with per-operation concurrency/backpressure bounds retained.

# Execution

Inputs: the acceptance above, [BenchmarkComparisons](../Features/BenchmarkComparisons.md)
and [ADR-064](../ADR/ADR-064-three-pipeline-release-delivery.md).
Scope and test strategy are approved by the owner's direct workflow correction;
this applies the existing three-pipeline design without topology/API changes.

Task graph:

| Task | AC | Owner / tier | Permission | Dependency/start | Artifact / verification | State/join |
|---|---|---|---|---|---|---|
| TASK-UB-01 | 001-004 | lead / planning | workflow/policy/ADR/docs writes | acceptance exists | scoped source diff, static governance | active |
| TASK-UB-02 | 002 | workload review / inherited high capability | read only | plan exists | exact inventory, no omitted suite | pending; lead review |
| TASK-UB-03 | 001/003 | regression review / inherited high capability | read only initially | plan exists | proposed TUnit ownership/gates | pending; lead review |
| TASK-UB-04 | all | lead / integration | scoped commit/push, GitHub dispatch | joined source review | exact-SHA CI/Benchmarks jobs/artifacts | pending |
| TASK-UB-05 | 005 | bounded Luna worker / high | exact11 contract/oracle files | approved frozen display map | scoped diff,6 JS syntax checks | complete; lead reviewed |
| TASK-UB-06 | all | independent high-capability review | read only | joined source | graph/name/provenance/regression review | active |

Ordered steps:
- [x] Record owner correction and scope/acceptance before source edits.
- [x] Inspect current GitHub CI/Benchmarks full baseline; track each actual failure.
- [x] Join independent workload and regression discovery. Implementation writes
  are confined to a new regression file; shared workflow/contracts remain lead-owned.
- [x] Add regression first, move pinned-image test/artifact into common image job,
  remove standalone branch and route qualify solely through the complete aggregate.
- [x] Update ADR implementation/evidence traceability; inspect complete scoped diff.
- [x] Static governance and YAML/JS source checks. No local test qualification.
- [ ] Commit/push only task changes on existing main, dispatch/inspect exact-SHA
  CI and Benchmarks; retain real jobs/artifacts and update failures without skips.

Baseline: CI37117886564/ec3399ef279fda1f4cf510e5ce2a0650684776f9 passed.
Benchmarks37117886617 on that same source failed comparison-images native
topology-model checks. CI37118356012 and Benchmarks37118356008 at86e58128a were
still running during inspection; the latter's build/images/pinned-image jobs pass
and native preflight starts. Keep these actual states, not overall success claims.

- [x] Baseline native topology-model failure: known Timescale registry pin repaired
  by existing86e58128a; actual later source comparison-images job succeeds. This
  task does not claim completion of the ongoing native cohort.
- [x] Static shellcheck SC2016 informational finding exists identically in original
  and edited Release literal Markdown printf body. Preserve literal backticks;
  YAML/action validation passes, no new finding and no source suppression.
- [ ] Exact delivered-source CI and complete Benchmarks outcome remain pending.

Independent review confirms270 lacks TimeSeries, and intensive6/30 delivery is
unfinished. Existing KurrentStreamOwnershipTests is now explicitly executed;
legacy mixed-engine smoke/TimeSeries and BDN are not relabelled isolated native
performance. Their broader product qualification gates remain open. No local
tests/benchmarks; no installed skill applies.
Validation order: source/schema review, static governance, GitHub build/format/
ordinary TUnit suite, real Docker/image/native-cell qualification, authenticated
aggregate and unchanged website qualification/deployment gates. Accepted ADR
status remains until required provider evidence exists. Release stays manual.
