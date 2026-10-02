# AI database SQL implementation plan

Sources: [brainstorm](ai-database-sql.brainstorm.md),
[acceptance](ai-database-sql.acceptance.md), [ADR-054](docs/ADR/ADR-054-central-sql.md),
[ADR-055](docs/ADR/ADR-055-typed-relational-rows.md).

Owner authorizes unified SQL, relational behavior and performance source work.
The lead accepts these bounded implementation contracts under that direction;
future JOIN/FK/fused operator contracts remain separate explicit product stages.
Strong planning/review remains with root. Workers inherit the high-capability
model: these small scopes still touch persisted schema, security or admission;
no reliable cheaper-tier correctness/routing evidence is available in this run.

## Ordered task graph and completion

| Task / dependencies / start | Owner / permissions | AC | Artifacts, verification, join |
|---|---|---|---|
| TASK-AISQL-001/002/003; immediately | sql/models/gates audit; read-only | 001,009,010 | Complete source findings + exact baseline CI; discovery joined |
| TASK-AISQL-004; discovery | root only; docs and all shared joins | all | Acceptance/ADRs, public DTOs and architecture map frozen before writers |
| TASK-AISQL-005; 004 + shared DTOs | models worker; Core/Features/RelationalStorage and UnitTests/Features/RelationalStorage only | 002–004 | First tests, validators; no edits to shared engine/resource/docs; root joins validation calls |
| TASK-AISQL-006; 004 + shared DTO | SQL worker; Server/Features/QueryExecution/Sql* and UnitTests/Features/QueryExecution/SqlOperation* only | 005–007 | First parser/binder tests, strict bounded CALL/SELECT compiler and MCP descriptor; root joins catalog/API/client/admission |
| TASK-AISQL-007; 004 | performance worker; QueryCandidateReader.cs and QueryExecution/EqualityAccessPath* tests only | 008,010 | First real-store regressions then symmetric equality extraction; root reviews semantics |
| TASK-AISQL-008; all workers complete | root; public contracts, engine hooks, client/server/MCP/shared admission, RF3 tests | 002–009 | Disjoint diffs reviewed, integrated source, honest status and exact-SHA CI |
| TASK-AISQL-008B; reviewed 008 source | gates worker; NEW QueryExecution/SqlRf3Delivery*, SqlRf3CancellationTests.cs, SqlRf3AdmissionTests.cs only; root owns shared ClusterFixture/protocol setting projection | 006/007 | Actual SDK/MCP SQL receive/ACK fencing, cancellation/next-call health and insufficient heavy DATA admission with direct-control progress; root reviews and joins. Concurrent saturation remains separate qualification |
| TASK-AISQL-009; joined source | review worker; read-only high capability | all | Independent policy/security/atomicity/admission review; source findings, no local tests or mutations |

No worker may expand scope, change contracts/policy, install tools/dependencies,
stash, weaken diagnostics/assertions, test locally or commit/push. Escalate any
contract conflict. Shared contracts/configuration/docs have exactly one owner.

- [x] Read current policy/map; preserve dirty concurrent site files.
- [x] Run read-only independent SQL/models/gates audits and join findings.
- [x] Write acceptance and Accepted ADR implementation contracts before code.
- [x] Freeze additive DTOs and hand disjoint approved write scopes to workers.
- [x] Establish actual baseline: run37065200835 at b21c9ee completed failure;
  all three OS verification/analyzer jobs passed, RF3 45/46 and comparisons2/4
  passed. Dispatched37066160501 was cancelled without jobs. Failure details below;
  baseline collection is complete, baseline qualification is not green.
- [x] Author regression sources before implementation; CI-only execution. A failing runtime baseline is not established by source order.
- [x] Join schema validation, strict SQL compiler and symmetric index selection.
- [x] Add public .NET/MCP RF3 differential/authorization/retry/linkage sources.
- [x] Join delivery-token, pre-cancellation and insufficient-DATA-budget RF3 sources after independent/root review. In-flight cancellation and concurrent lane saturation remain separate unproved qualifiers.
- [x] Run static governance and review combined diffs; governance and both staged/working whitespace checks pass. Independent SQL and model/SDK review complete; root reviewed the final delivery/cancellation/admission joins. No local tests.
- [ ] Commit scoped coherent code/docs/tests on current checkout; preserve unrelated
  concurrent benchmark and root-policy changes. Push an isolated candidate ref
  for exact-SHA qualification before delivery of stable changes to main; no force/protection bypass.
- [ ] Qualify exact SHA via ci.yml: build -> TUnit units + recovery/RF3 -> format
  and remaining gates; workflow retains its own configured order. Inspect and fix
  each failure, repeat relevant/full required jobs and retain artifacts.
- [ ] Update README/status/evidence after actual outcomes. ADRs stay Accepted
  while runtime/coverage/performance gates or later product stages remain open.

## Existing baseline failures

Main SHA b21c9eeaed701b7b2e267d4690fb3ac302c06c17: run37065200835 queued at
discovery. Predecessor959a4c822 run37064132315 has Windows unit + RF3 failures;
comparison still running. Current exact baseline is being inspected; individual
tests will be added when actual outputs are available. Historical passing
9c570f8 does not qualify this source or current expanded workflow.

## Verification methodology and risks

Pure compiler rejection and capability metadata cases; real transactional schema
and equality access-path tests; real Docker/Aspire SDK+official-MCP differential
flows; persisted authority negative/retry/cancellation/bound cases. Complete
Release analyzer build, formatter and governance cannot be inferred from narrow
source checks. Skill Orleans: preserve actors/quorum/node ownership through the
existing gateway; no new provider, reentrancy or state. Shared-budget performance
proof needs raw exact-SHA CI JSON; scalar-disabled pass tests portability only.
Missing numeric coverage collection and endurance/power-loss gates remain open.

Full product follow-up stages: authorized bounded SQL JOIN and typed FK/check
integrity, declarative SELECT sources for events/queue/search/graph/blob/time
series, unified cross-model reference validation, and dedicated RF3 benchmarks.
No plan item or acceptance criterion is marked satisfied by a worker claim alone.

TASK-AISQL-001–007/009: complete source/discovery/review artifacts, no runtime
qualification implied. TASK-AISQL-008/008B: source integration complete, exact-SHA
CI/delivery dependencies still open. All workers have completed their bounded scope.

## Baseline attempt and tracked previous failures

Baseline37066160501 at b21c9ee was cancelled before any jobs. No passing baseline
is inferred. Prior completed37063262333 must not qualify the new source:

- [ ] RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes: unexpected RecoveryRequired; inspect retained node/catch-up receipts, current source/run first.
- [ ] AcAd007RealSdkAndOfficialMcpAgreeOnCatalogAndNonconsumingQueue:92vs91 cut equality; successor959a4c8 source compares logical data. Current exact-SHA proof remains required.
- [ ] AcSeries012FollowerRestartRetainsLatestAndWindowValuesOnEveryReplica: failed commit; inspect authority/quorum outcome before changing test.
- [ ] AcSeries010WindowsAreDenseAnchoredUtcAndClampThePartialFinalWindow: failed setup; inspect actual domain error, never accept false success.
- [ ] PublishedImageDamageFailsClosedAndPreservesNewerCanonicalState(SnapshotInstalled, True): Windows cancellation; qualify existing new trial-admission source, preserve fault semantics.
- [ ] AspireRunsIdenticalScenariosAgainstRealRf3AndExternalEngines: comparisons resource Waiting; qualify current caller/composition repairs without weakening lifecycle.
- [ ] AspireTimeseriesProfileEmitsOracleReportAndProtectsForeignSchema: comparisons resource Waiting; inspect real Timescale/profile startup and qualified report.

These are historical failures, not a claim that later exact source still fails.
All new-source job/test outcomes are tracked after the candidate is delivered.

## Exact pre-change baseline now available

[37065200835](https://github.com/managedcode/KeyLoad/actions/runs/37065200835)
at b21c9eeaed701b7b2e267d4690fb3ac302c06c17 completed failure. Complete three-OS
build/format/governance/unit/scalar/recovery and analyzer jobs passed. Required
runtime failures remain:

- [ ] RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes:
  [RF3 job111033735266](https://github.com/managedcode/KeyLoad/actions/runs/37065200835/job/111033735266),45/46 passing, no skips. RecoveryRequired after stopped replica; actual node1 logs show distributed-directory/placement connection rejection to node2. Inspect transport failure classification; do not broaden accepted retry errors to hide genuine recovery damage.
- [ ] AspireRunsIdenticalScenariosAgainstRealRf3AndExternalEngines:
  [comparison job111033735452](https://github.com/managedcode/KeyLoad/actions/runs/37065200835/job/111033735452), resource Waiting with no creation/health; lifecycle timed out. Concurrent comparison work is outside this scoped source commit.
- [ ] AspireTimeseriesProfileEmitsOracleReportAndProtectsForeignSchema:
  same comparison job, resource Waiting; retained20 attempts/20 correctness checks pass but lifecycle still fails. Job2/4 passing, no skipped tests; later measurement steps skipped.
- [ ] KeyLoad/StreamAppend smoke repetitions0/1: retained96 cases contain46 measured/48 unsupported/2 failures from IComparisonSession.ReadEventAsync NotSupportedException. Diagnose the owning comparison caller; no performance conclusion from partial artifacts.

Reports are retained locally under /tmp/keyload-baseline-37065200835-artifacts;
GitHub job artifacts remain the durable authority. These failures are tracked
independently from new SQL correctness; candidate gates must still include them.

## Candidate qualification loop

Candidate6cfdadf38ec527472ef51aadf1ffe8b716a41587 was scoped-committed and pushed to
codex/ai-database-sql-20261002; [run37068157832](https://github.com/managedcode/KeyLoad/actions/runs/37068157832)
is the exact first-source qualification. Comparison build job111041008847 and RF3
build failed before test execution on public RelationalColumnType member names:

- [ ] CA1720 Int64 identifier: rename to WholeNumber preserving signed64 ordinal/contract, update actual validator/test callers; repeat exact-SHA build/complete gates.
- [ ] CA1720 Decimal identifier: rename to FixedPoint preserving exact decimal ordinal/contract, update actual validator/test callers; repeat exact-SHA build/complete gates.

ADR-055 and acceptance are refined before the naming implementation. No analyzer
suppression, existing-test weakening, local test run or result inference.

## Joined-source review findings

- Fixed stale nullable-result assertion to include the dynamic SQL adapter while
  preserving the four canonical absent-object contracts.
- Fixed independently frozen public BlobStorage catalog count for the additive SQL tool.
- AC-AISQL-005/ADR-054 now explicitly preserve the planned single Q1 parser:
  CALL grammar/target/envelope rejects before gateway; SELECT grammar rejects in
  the authorized canonical actor before any scan/effect.
- Actual SQL receive/ACK-fence, cancellation and insufficient heavy DATA admission
  RF3 sources are TASK-AISQL-008B. The root projects actual existing HTTP reserve
  settings into the dedicated fixture so SDK/MCP SQL rejection and direct-control
  progress can be deterministic. Concurrent occupied-lane saturation stays a
  separate qualifier; AC007 stays open until actual GitHub runtime evidence.
