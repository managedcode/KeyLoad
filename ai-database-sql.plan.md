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
| TASK-AISQL-011; exact baseline finding and Accepted ADR-036 contract | SQL worker; OrleansNode.cs, NEW Server/Features/ClusterRouting/OrleansRpcFailure.cs and UnitTests/Features/ClusterRouting/OrleansRpcFailureTests.cs only; root owns trusted gateway/auth caller joins and docs | 011,009 | Native-exception regression sources, narrow initial-RPC classification, no retry/suppression/domain recoding; root reviews then exact-SHA build/unit/RF3 catch-up proof |
| TASK-AISQL-012A; Accepted ADR-054 integration repair | models worker; Client/Features/BlobStorage/BlobClient.cs and NEW UnitTests/Features/BlobStorage/BlobSdkArgumentTests.cs only; root owns Send access join | 012,006,009 | Tests-first public null guards, source-compatible feature extension methods, same transport/IDs; root review + exact build/RF3 |
| TASK-AISQL-012B; Accepted ADR-054 integration repair | gates worker; QueryCandidateReader.cs only | 008,009 | Remove depth4 nesting without new allocations/semantic changes; existing25 real-store regression cases + exact build |
| TASK-AISQL-018; root static scenario review | models worker; RelationalSchemaTests.cs only | 002,004,009 | Ensure schema-change fixture is independently valid (retain indexed name column), then assert exact UnsupportedCapability and unchanged persisted schema; malformed-index validation remains separately tested. No production precedence change or assertion weakening |
| TASK-AISQL-023; existing AC-RC-003/AC014 contract before code | gates worker; RecoveryTests.cs RunSeededCrashTrialAsync call placement only | 014,009 | Receipt immediately after successful real atomic/durable assertions inside originaltry before mandatorycleanup, same15slinkedtoken/20x50trials/4slots; existingcatch capturesreceiptfailureidentity, no newtimeout/skip/looserassertion; exact3OSrecovery+1000receiptsperOS join |
| TASK-AISQL-020; Accepted ADR-036/AC013 refinement before code | SQL worker; GrainReplyFactory.cs, RequestGrain.cs, DatabaseReadGrain.cs, CommandPartitionGrain.cs and new GrainReplyCancellationTests.cs only | 013,011,009 | Genuine caller-token cancellation tests first, sole classifier and3 actual token joins; preserve domain errors, command unknown outcomes, current closed-category logging/privacy and unchanged retry assertions; root reviews and exact-SHA complete gates join |
| TASK-AISQL-019; exact formatter diagnostics | root; SqlRf3DeliveryTests.cs and OrleansRpcFailureTests.cs only | 006,011,009 | Split object initializer members and explicitly qualify the one native messaging exception to remove conflicting import ordering; no semantic/test changes; exact-SHA formatter join |
| TASK-AISQL-015; exact UnitTests compiler diagnostics | models worker; RelationalLinkageTests.cs, RelationalTestData.cs and RelationalRowTests.cs only | 002–004,009 | Correct actual ordered TUnit collection comparison and cache CompositeFormat without altering exact raw decimal fixtures or assertions; exact-SHA compiler/units join |
| TASK-AISQL-016; exact UnitTests compiler diagnostics | SQL worker; OrleansRpcFailureTests.cs, SqlOperationBudgetTests.cs and SqlOperationCompilerTests.cs only | 005,007,011,009 | Await native CancelAsync before cancellation assertions and remove imports proven redundant by actual compiler; same errors and test coverage, no suppression |
| TASK-AISQL-014; exact RF3 compiler diagnostics | gates worker; SqlRf3AdmissionTests.cs only, root owns static scenario member | 004,007,009 | Isolate existing per-node using/await-using ownership in VerifyNodeAsync outside fixture try/finally/loop analysis; start caller deadline after separately bounded fixture readiness; preserve all3 real-node assertions, same-ID control progress and schema/linkage contracts; no suppression/test weakening |

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
- [ ] Repair baseline initial Orleans RPC classification under AC-AISQL-011/ADR-036, join trusted intent and native-exception regression sources, then qualify exact SHA. Never broaden election retry acceptance to conceal storage recovery errors.
- [x] Join reviewed TASK-AISQL-011/012A/012B source: native RPC classification, public SDK guards/feature extensions and reduced query nesting. Independent TASK-AISQL-013 source review complete; runtime/build qualification pending.
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
  [RF3 job111033735266](https://github.com/managedcode/KeyLoad/actions/runs/37065200835/job/111033735266),45/46 passing, no skips. RecoveryRequired after stopped replica; node1 logs also contain distributed-directory/placement connection rejection to node2, but the exact failed request dispatch phase and native exception class were not captured. The narrow native RPC repair is a contract correction, not proof of that failure's root cause; qualify the unchanged catch-up test and preserve genuine recovery damage.
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

Candidateb3f93431ad1a2c2465470884c2a1daa6528e498b / [37068458582](https://github.com/managedcode/KeyLoad/actions/runs/37068458582)
reached further source compilation and exposed:

- [ ] CA1062 six new BlobClient write methods: add public null guards through accepted feature extensions, author null tests first, qualify actual same-ID RF3 lifecycle.
- [ ] KLD0031 aggregate KeyLoadClient215 lines: move the feature-owned blob methods to BlobClientExtensions using one internal Send join; no partial-type splitting to evade aggregate policy.
- [ ] KLD0033 QueryCandidateReader.Equalities depth4: early-exit iteration removes nesting with unchanged equality/filter/budget semantics and existing regressions. No limit exception or analyzer suppression.

Candidateaf9e0d16b6fdc781f7d4b9b54cd56fec152cfc56 / [37069241980](https://github.com/managedcode/KeyLoad/actions/runs/37069241980)
reached Server build after those repairs and stopped on one unnecessary import:

- [ ] IDE0005 OrleansRpcFailure.cs namespace import: remove redundant using;
  native exception scope/behavior unchanged. Repeat complete exact-SHA gates.

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

Candidate9f3acf5b9af39ad5d8f818039b22eb8ce1b0463e / [37069574976](https://github.com/managedcode/KeyLoad/actions/runs/37069574976) repeats the full gates after the IDE0005 repair. Outcome is pending; no runtime result is inferred from source review.

RF3 build job111045550869 reached IntegrationTests and exposed:

- [ ] CA1822 RelationalSqlRf3Scenario.Space: make the constant-only helper static; linked graph/vector semantics remain identical.
- [ ] CA2000 SqlRf3AdmissionTests caller allocation: the committed loop already uses using/await-using, but analyzer reports both declarations within fixture try/finally. Extract an explicit per-node disposal scope into VerifyNodeAsync; retain unconditional disposal, all three node assertions and original control semantics. Start the caller deadline after the independent bounded fixture initialization so cold startup cannot consume it. Repeat exact-SHA build and real RF3 tests.

The same candidate Ubuntu job111045551116 exposed additional unit source failures before any units could execute:

- [ ] CS0103 RelationalLinkageTests.CollectionOrdering: bind the actual ordered TUnit comparison API; preserve sequence equality.
- [ ] CA1863 RelationalTestData/RelationalRowTests string.Format: cache CompositeFormat preserving byte-for-byte numeric JSON fixture contents.
- [ ] CA1849 OrleansRpcFailureTests/SqlOperationBudgetTests cancellation: await native CancelAsync before exercising the pre-cancelled boundary.
- [ ] IDE0005 unused UnitTests imports in OrleansRpcFailureTests/SqlOperationCompilerTests: remove only compiler-identified redundant imports.

TASK-AISQL-014/015/016 are complete source artifacts, reviewed and joined by root. Per-node disposal extraction, static vector-space helper, actual TUnit namespace, cached invariant formatting, awaited cancellation and unused-import removals retain every contract/assertion. Exact-SHA build/tests remain pending.

Candidate3a744d19aa4443a522d303d8d91257f2e20396da / [37070004864](https://github.com/managedcode/KeyLoad/actions/runs/37070004864) contains reviewed TASK-AISQL-014/015/016 repairs. The superseded9f3 run37069574976 was explicitly cancelled after recorded required build failures to release same-ref CI concurrency; its unfinished comparisons cannot count as passing evidence. The new run must execute every required suite.

Root source review found the schema-migration test removed the indexed name column while retaining its index. ValidateResource correctly rejects that malformed schema before checking migration. TASK-AISQL-018 changes only the intended valid changed-schema fixture and proves it validates independently; migration refusal and unchanged original persisted schema must remain exact. CI3a744d19a is already running, so its observed result remains authoritative and the repaired fixture needs a later exact-SHA run.

TASK-AISQL-017 independent review is complete/clean for all11 original compiler diagnostics. TASK-AISQL-018 is source-complete and root-reviewed: valid nullable Name change with preserved indexes, explicit schema validity, exact migration refusal and byte-for-byte persisted-resource equality. GitHub execution remains required.

Candidatec3eeb2b7a512f31e3489cef44b6127d9d280e836 / [37070513242](https://github.com/managedcode/KeyLoad/actions/runs/37070513242) includes reviewed TASK-AISQL-018 and is queued behind3a744d19a. The3a candidate has passed Linux/macOS solution builds, analyzer-rule tests and IntegrationTests compilation; RF3 runtime/formatter/remaining verification are in progress. These partial successes do not qualify the complete stage.

Candidate3a744d19a / run37070004864 Ubuntu job111047630094 passed full build and real process recovery136/136 with no skips. Formatter failed on SqlRf3DeliveryTests95/96 two same-line initializer members and OrleansRpcFailureTests import order. Units/scalar units were skipped after format failure, so they are not qualified. TASK-AISQL-019 source fixes only those exact diagnostics; full gates must repeat.

Actual3a744d19a RF3 artifact SHA matches. Job111047630080 passed59/60, no skips; all14 new SQL/relational scenarios passed. Existing retained-replica catch-up failed17.976sec with server Cancelled detail, not SDK write timeout. Required accepted retry errors are unchanged. Investigate caller-token vs internal native read cancellation distinction; initial-RPC source correction does not by itself prove the original cause. Receipt: docs/implementation/ai-sql-qualification-37070004864.json.

TASK-AISQL-020 approved before implementation: server-sourced Cancelled in retained-replica test exposes read OCE classification without incoming-token context. SDK write transport mapping is already correct. Per-fixture failure names can overwrite prior tails, so exact causal phase remains unproven; concurrent diagnostic work is preserved. No native-helper expansion, SDK workaround or broader election retry acceptance.

Run37070004864 completed failure: complete three-OS builds/analyzer-rules pass, known formatter failures; Linux process recovery136/136, macOS recovery passes pendingcount, Windows recoveryfails under read-only audit. RF359/60 all14newpass; comparisonsfail under audit. Supersededc3eeb2b7a run37070513242 cancelled because parent formatter/cancellation defects remain and reviewed repairs must be qualified together; cancellation/unfinished jobs never count as passing.

TASK-AISQL-021 read-only audit complete: Windows135/136, failurebatch7/seed1708 at evidence.WriteLineAsync aftersuccessfulatomicassertions and cleanup. The actualdeadline source/trial remainunproven. TASK-AISQL-023 accepts existing Receipt-before-Cleanup contract correction only; no storage/dependency fault is inferred or hidden.

TASK-AISQL-022 completed read-only retained-artifact audit: comparison reports bind exact3a; smoke96cases46measured48unsupported2failed (KeyLoadStreamAppend native adapter defaultReadEventAsync),552measuredsamples succeeded, TimeSeries20attempts+20correctnesspass but both publicAspiretestsWaiting/noCreation/Health. Job2/4passing/no skips. Committedcomparisoncaller/compositionunchangedfrombaseline; concurrentowningworkpreserved. No SQL-specific performance claim or passing comparison gate.

TASK-AISQL-020/023 source artifacts are complete and root-reviewed; independent gates review is clean. Source corrections preserve every domain error, actual actor token, unknown command outcome, native diagnostics privacy, original crash trial bound/receipt fields/cleanup and unchanged RF3 retry expectations. TASK-AISQL-019 exact formatter source repair joined. No complete runtime gate is inferred; new exact-SHA CI and1000 native crash receipts perOS remain required.

Candidate722e7fe7872288d99869b6e25950e67a547b7d86 / [37072003906](https://github.com/managedcode/KeyLoad/actions/runs/37072003906) contains reviewed TASK-AISQL-019/020/023 and complete source-bound prior evidence. Exact full qualification is running; no result is inferred yet. Concurrent benchmark, provider, diagnostics, site and unrelated status changes remain visible and excluded from this scoped commit.
