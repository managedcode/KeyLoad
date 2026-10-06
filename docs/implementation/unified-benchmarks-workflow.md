# Historical shared parallel Benchmarks delivery

This is an immutable historical development record, not the active workflow
contract. The current four workflows and final bounded Website dispatch are
defined by [ADR-064](../ADR/ADR-064-workflow-release-delivery.md),
[ADR-112](../ADR/ADR-112-independent-website-publication.md) and
[BenchmarkComparisons](../Features/BenchmarkComparisons.md).

The recorded source changes implemented the then-current owner correction. This record
distinguishes configuration/source checks from actual GitHub qualification.
The root brainstorm/acceptance/working plan are task-local ignored files; the
durable delivery contract and evidence are retained here.

# Acceptance

Goal: Benchmarks shows TimeSeries checks within common image preparation and runs
the existing performance cohort in parallel, with no separate TimeSeries branch. Existing owner direction
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
and [ADR-064](../ADR/ADR-064-workflow-release-delivery.md).
Scope and test strategy are approved by the owner's direct workflow correction;
this applies the existing three-pipeline design without topology/API changes.

Task graph:

| Task | AC | Owner / tier | Permission | Dependency/start | Artifact / verification | State/join |
|---|---|---|---|---|---|---|
| TASK-UB-01 | 001-004 | lead / planning | workflow/policy/ADR/docs writes | acceptance exists | scoped source diff, static governance | complete; integrated |
| TASK-UB-02 | 002 | workload review / inherited high capability | read only | plan exists | exact inventory, no omitted suite | complete; reviewed |
| TASK-UB-03 | 001/003/005/006 | regression review / inherited high capability | three owned regression files | plan exists | TUnit graph/name/completeness assertions | complete; reviewed; GitHub ordinary unit suite passed |
| TASK-UB-04 | all | lead / integration | scoped commit/push, GitHub dispatch | joined source review | exact-SHA commits/jobs/artifacts and honest outcomes | complete; source delivered |
| TASK-UB-05 | 005 | bounded Luna worker / high | exact11 contract/oracle files | approved frozen display map | scoped diff,6 JS syntax checks | complete; lead reviewed |
| TASK-UB-06 | all | independent high-capability review | read only | joined source | graph/name/provenance/regression review | complete; no source defect found |
| TASK-UB-07 | 002/003 | lead and independent native review | read only | real GitHub execution | complete cohort/CI qualification | failed; native failures and superseded run, no publication proof |

Ordered steps:
- [x] Record owner correction and scope/acceptance before source edits.
- [x] Inspect current GitHub CI/Benchmarks full baseline; track each actual failure.
- [x] Join independent workload and regression discovery. Implementation writes
  are confined to a new regression file; shared workflow/contracts remain lead-owned.
- [x] Add regression first, move pinned-image test/artifact into common image job,
  remove standalone branch and route qualify solely through the complete aggregate.
- [x] Update ADR implementation/evidence traceability; inspect complete scoped diff.
- [x] Static governance and YAML/JS source checks. No local test qualification.
- [x] Commit/push only task changes on existing main, dispatch/inspect exact-SHA
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
- [ ] Complete delivered-source CI and Benchmarks qualification remain unproven.
- [x] CI37119418418/45e4992d86fcb16d1568327e1d38e9a8e76140ec: new
  WorkflowStepNameTests async test synchronously reads its composite file (CA1849).
  Repair with awaited cancellation-aware file IO; preserve every assertion.
- [x] Same exact-source CI: WorkflowLayoutUnifiedPerformanceTests synchronously
  reads its JSON contract (CA1849). Repair with awaited cancellation-aware IO.
- [x] Same exact-source CI: nodeCounts JSON machine key violates KLD0001. Extract
  the named property constant; do not suppress the repository analyzer.

Source milestone45e4992d8 was committed/pushed to main with only task-owned files
and three own root-policy lines. Unrelated SQL/storage/package working changes
remain unstaged. [CI](https://github.com/managedcode/KeyLoad/actions/runs/37119418418)
finds the three authored-regression build diagnostics above.
[Benchmarks](https://github.com/managedcode/KeyLoad/actions/runs/37119418409)
actually starts Plan benchmark runs, Build and check KeyLoad, and Build Docker
images concurrently with the new readable names. This is observed graph execution,
not a passing whole benchmark cohort. The old86e58128a benchmark run was cancelled
as superseded to unblock this exact-source run; its partial results are not proof.

Independent review confirms270 lacks TimeSeries, and intensive6/30 delivery is
unfinished. Existing KurrentStreamOwnershipTests is now explicitly executed;
legacy mixed-engine smoke/TimeSeries and BDN are not relabelled isolated native
performance. Their broader product qualification gates remain open. No local
tests/benchmarks; no installed skill applies.
Validation order: source/schema review, static governance, GitHub build/format/
ordinary TUnit suite, real Docker/image/native-cell qualification, authenticated
aggregate and unchanged website qualification/deployment gates. Accepted ADR
status remains until required provider evidence exists. Release stays manual.

# Delivered-source GitHub evidence

Repair commit `995721374fe3afa9af8b25e69fc438909f768870` fixes the three
authored-regression diagnostics above with cancellation-aware awaited IO and named
JSON property constants. No assertion or analyzer rule was weakened.

[CI37119641948](https://github.com/managedcode/KeyLoad/actions/runs/37119641948)
on that exact SHA passed repository governance, the compiler analyzer fixture,
Release build, formatting, ordinary TUnit unit tests and the genuine RF3 .NET/MCP
client job. Its scalar unit step was cancelled and process recovery never ran when
concurrent main delivery superseded this run. Overall conclusion is cancelled;
this is not full CI qualification.

[Benchmarks37119641910](https://github.com/managedcode/KeyLoad/actions/runs/37119641910)
on the same SHA passed build/checks, plan and common image qualification. The actual
plan artifact records270 cells (108 CRUD,162 specialized),9 targets and node
counts1/2/3. Common image qualification passed the relocated TimeSeries pinned-image
probe and retained its facts. Real benchmark CRUD jobs ran while native preflight
jobs were still running, confirming the matrices are independent. Runner capacity
can queue admitted jobs; workflow dependencies and caps no longer serialize them.

This run retained native failures before being superseded/cancelled:

- [KeyLoad one-node preflight](https://github.com/managedcode/KeyLoad/actions/runs/37119641910/job/111194069823)
  failed registry readiness during native image import. Both images loaded and
  identity checks passed; the owned registry listened within one second and was
  still running when the importer failed after its30-second readiness bound.
  No push began. Retained native commands, registry log/state and cleanup receipts
  do not expose the original HTTP failure because readiness discards exceptions.
  The readiness source is unchanged by this repair; transport cause is unproven.
- [KurrentDB two-node preflight](https://github.com/managedcode/KeyLoad/actions/runs/37119641910/job/111194069852)
  retained five measured StreamAppend repetitions, each10000 successes with no
  operation failures. Its subsequent volume regression expected empty original
  event metadata but observed76 bytes. Event identity/revision/data/type checks
  passed immediately before that assertion. Native reader/fixture/event creation
  source is identical before and after this repair; absent metadata bytes prevent
  attributing the server/SDK/oracle cause. The three-node preflight also failed its
  workload step and does not count as qualified.

No failed, skipped, cancelled or missing cell refreshes website metrics. Aggregation,
website qualification/publication, scalar portability, process recovery and the
unfinished intensive TimeSeries measurement delivery remain unqualified here.
Concurrent main advanced to `864aa37809088a86d841b08cda9e98dc0639a567`; its
separate CI/Benchmarks outcomes cannot be attributed to these two repair commits.
