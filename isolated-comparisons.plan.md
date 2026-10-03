# Isolated Linux comparisons plan

Derived from [brainstorm](isolated-comparisons.brainstorm.md) and [acceptance](isolated-comparisons.acceptance.md). Root approves this implementation contract under explicit owner2026-10-03 direction. Canonical slice BenchmarkComparisons; replication refinement remains ClusterReplication. Required ADR-056 and ADR-007/034/040 amendments precede relevant source edits.

## Ordered graph / ownership

| Task | AC | Owner / model / permissions | Dependency / start | Artifacts / verification / join state |
|---|---|---|---|---|
| TASK-ISO-001 | 002..006 | sql_audit, inherited capable planner, read-only | independent discovery | source report complete; no local execution |
| TASK-ISO-002 | 006..009 | models_audit, inherited capable planner, read-only | independent discovery | ingestion/provenance report complete |
| TASK-ISO-003 | 001..009 | gates_audit, inherited capable planner, read-only | independent discovery | workflows/source report complete |
| TASK-ISO-004 | all | root architecture/integration, shared write owner | join001..003 | approved contract/ADR/task graph; static governance |
| TASK-ISO-005 | 003/004 | sql_audit, inherited capable tier, disjoint specified topology files/tests | approved004+shared enum | benchmark-only fixed-voter validation/KeyLoad proof; source review then exact-SHA Linux |
| TASK-ISO-006 | 006/007 | models_audit, inherited capable tier, only new aggregate modules/tests | approved004+frozen envelope | strict raw-envelope/plan merge validator and TUnit actual Node process tests; root join |
| TASK-ISO-007 | 001/002/007 | gates_audit, inherited capable tier, new matrix/control modules/tests only | approved004 | closed270-cell plan split108CRUD/162special, workflow dependency proposal; root join |
| TASK-ISO-008 | 005/006 | root shared contracts/runner/dataset/host/test integration |004, serialized shared ownership | selected engine/scenario, serious CRUD and worker report envelope |
| TASK-ISO-009 | 002/003 | next disjoint native adapter/resource workers |005+008 frozen constructors | genuine native1/2/3 engine resources/membership/ACK; no foreign engines |
| TASK-ISO-010 | 006/007 | root workflow/fixture/auth join |005..009 complete | one cell perLinux VM, raw+image+native receipts, actual GitHub cohort |
| TASK-ISO-011 | 008/009 | disjoint site/evidence workers, root shared tokens/workflows/inventory |006+010 qualified cohort | schema4 site/provenance/browser/coverage integration and separate Pages |
| TASK-ISO-012 | all | root strongest final integration | all required workers complete | exact-SHA required checks, complete full cohort, aggregation/site/publication evidence |

High-capability inheritance is retained for replication/JSON evidence/topology/trust implementation because these are cross-boundary correctness-critical scopes and no concrete cheaper model capability evidence is available. Workers must stop on contract drift, public/schema invention, dependency defects, ownership overlap or unexpected baseline failure; no local tests/builds/benchmarks, commits, suppression, fake dependencies, skipped required gates or expanded write scope. Root reviews all diffs, source identity and combined gates.

## Baseline and known failures

- [x] Inspect full relevant real baseline after planning input: GitHub37072003906 SHA722e7fe7872288d99869b6e25950e67a547b7d86, completedfailure. Linux/macOS/Windows full verification and analyzer job success; comparison and RF3 failed. Historical threeOS success is retained; new required execution isLinux.
- [ ] RF3 `ReplicatedAtomicBatchSurvivesLeaderContainerKillAndMinorityRejectsWrites`: intentional restart health wait fails while fresh Docker container running. Stale terminal snapshot strongly supported, selected event generation unproven. TASK-AISQL-024 has exact source/receipt audit; native bounded recovery wait refinement remains required and cannot weaken health/SDK/data assertions.
- [ ] Comparison `AspireRunsIdenticalScenariosAgainstRealRf3AndExternalEngines`: old host-resource lifecycleWaiting/no exit, unsupported stream read in older source; current06a9b631 committed digest-image/public-read repairs need actual qualification.
- [ ] TimeSeries Aspire real profile lifecycleWaiting/no exit: same committed native image route awaits exact-SHA qualification; isolate target/cell rather than skip.
- [ ] Current site schema2/producer schema3 and full-nine target mismatch: update coupled schema4 producer/aggregate/site; do not weaken historical tests.

## Checklist and methodology

- [x] Record latest permanent owner rules without dropping existing policy.
- [x] Create brainstorm, detailed acceptance, task graph and frozen first-stage contract before code.
- [x] Add ADR056 and requirement/ADR007/034/040 amendments; freeze planner/proof/aggregate and native persisted membership safety before delegated edits.
- [ ] Add meaningful failing TUnit regressions derived from acceptance before production edits; exact-SHA GitHub only.
- [ ] Implement and review005/006/007 then shared008; validate static source/JSON/governance only locally.
- [ ] Integrate genuine native resource/adapters and isolated real-client case fixture; no current all-engine workflow remains as new performance proof.
- [ ] Run full solutionLinux restore/build, tests, recovery and RF3; fix each actual owning failure and preserve raw evidence.
- [ ] Run changed/related/full comparison case cohorts in GitHub. Native member/copy/ACK and CRUD oracle pass before each timing claim.
- [ ] Aggregate authenticated all-cell results; real-files negative tests and complete same-source cohort pass.
- [ ] Extend and run full siteTUnit/native/browser/coverage, format and provenance tests; regenerate site only from raw JSON.
- [ ] Deliver coherent scoped checkpoints/pushes; successful new producer/aggregation/site provider receipt before completion.
- [ ] Final strongest combined review: every AC/REQ/test/task/evidence linked, no pending mandatory gate, honest README/status.

Final validation order: approved Orleans skill for storage/routing/failure review; static governance/diff preservation; GitHub Release solution/analyzer build; fullTUnit/scalar; process recovery; real Docker/AspireRF3 and native1/2/3 correctness; complete intensive isolated cohort; formatter and required coverage/source inventory; authenticated aggregation; siteTUnit/browser/native thresholds; freshness/provider/live Pages proof. No configuration, worker claim or partial artifact counts as complete.

## Active source joins

- TASK-ISO-005 source reviewed: benchmark opt-in and atomic membership guard plus
  32 expanded authored cases; actual snapshot/restart/SDK/MCP qualification open.
- TASK-ISO-007 planner source reviewed:270 canonical cells,108/162 matrices;
  41 authored cases, none executed locally. Final CI/compiler review pending.
- TASK-ISO-008H sql_audit owns selected host/new IsolatedHost tests and serialized
  routing join; settings/envelope are frozen. Native adapters remain dependants.
- TASK-ISO-008M root owns fresh mutation corpus/preparation/readback, bounded
  sessions and sentinel. New corpus/oracle tests precede the source path.
- TASK-ISO-009QR gates_audit owns only Qdrant/Rabbit count/proof files and new
  NativeQuorumTopology tests. Explicit disabled Qdrant1 refinement is recorded.
- TASK-ISO-009PMOK next models_audit scope is approved in acceptance/ADR056:
  four native adapter prefixes and new NativeDocumentTopology tests; root alone
  owns native composition and authentic runtime qualification.
- Current06a9b631 baseline37073331174 completedfailure: image preflight failed
  before RF3/comparison execution, Linux unit failed, recovery/analyzer passed.
  Other checkout owner has a scoped image-format/schema regression repair; do
  not duplicate it or count skipped runtime stages as passing.

TASK010 image join discovered before cohort execution: existing prepare-images
builds independently in every job. Same source alone does not ensure identical
image bytes/config timestamps. The aggregate correctly requires a common immutable
generator image. Build server/generator once in a separate trusted source job,
retain native manifests/config/source receipt plus portable images, then import
and verify identical bytes into each job-owned registry/runner. No database or
live process is shared. Root must freeze import/proof wire and add real Docker
round-trip/digest/source negative tests in GitHub before enabling the cohort.

TASK-ISO-009PMOK source complete, root review pending. TASK-ISO-009PMOKR approved
composition contract above: models_audit owns four NEW native helper prefixes and
new model tests. Root retains all shared joins and genuine runtime qualification.

TASK-ISO-010I approved immutable v1 image bundle. gates_audit owns NEW bundle
modules and TUnit Node/files + actual Docker round-trip tests. Root owns transport,
workflow and canonical image exports. Same manifest bytes are a mandatory join.

## R14 compiler join

The full development build log /private/tmp/keyload-r14-integrated-build.log
contains142 distinct diagnostics, some already stale after active owning edits.
Read-only current-source triage is retained in
/private/tmp/keyload-build-join-triage-r14.txt. No compilation snapshot is runtime
qualification. Root approves only preserving compiler/style corrections under
the existing AC-ISO-002/006/007 contracts; spellings, validation inputs, scenario
count, assertions and subprocess/cancellation cleanup must remain identical.

| Task | AC / owner / model | Permissions / start | Artifact / verification / join |
|---|---|---|---|
| TASK-ISO-JOIN-PLANNER-R15 | ISO002/007; bounded Luna/high worker | Read current four IsolatedPlan test/process files; write ONLY a /private/tmp patch proposal and hash inventory, since another owner has active shared source | Named protocol/environment constants and exact-preserving analyzer/style fixes; no tests/build/source/Git mutations; root verifies fresh bytes before applying |
| TASK-ISO-JOIN-INTEGRATE-R15 | ISO002..007; root high capability | Serialized shared-file compiler/API/formatter joins, preserve fresh owning changes | Full combined development build/formatter/governance followed by actual final-SHA Linux gates; pending |
| TASK-ISO-SITE-COMPILER-R17 | ISO008/009; bounded Luna/high worker | Read current SiteIsolated*.cs and compiler log; write ONLY a private-tmp exact-byte-guarded patch proposal since another owner is active | Preserve every genuine browser/HTTP/arithmetic oracle,270 workers, all scenario/node/repetition/metric selectors and corruption cases; named fields/braces/var corrections only; root alone applies fresh reviewed bytes |

The final checkpoint follows the owner's all-current-KeyLoad-scope main request;
no active producer/resource/test file may be silently omitted, moved or stashed.

R19 source join: the new SiteIsolatedGitHub proof/receipt tests reference a
missing real Node-process bridge. Root alone joins the authored Node program to
the existing bounded SiteIsolatedNodeProcess.RunProcessAsync implementation;
temporary request/script files, exit/stderr checks, cloned JSON and cancellation
remain mandatory. This is the existing ISO007/009 native proof flow, not a new
protocol or provider substitute. Shared site ownership prevents safe delegated
source writes; root performs the bounded integration and reviews its whole diff.

TASK-ISO-010C native27 preflight approved before allocating full270 matrices.
Separate VMs/artifacts, fixed supported representatives, no replacement of final
qualification; root owns derived matrix and needs graph.

TASK-ISO-010G approved: gates_audit owns new native authenticated GH transport
modules/tests. Root owns CLI bindings/workflows and frozen proof join. Full270
collection plus native current-job capture are mandatory; parser inputs are not
authenticated proof.

TASK-ISO-011W approved compact projection/isolated loader/arithmetic/UI worker; root owns shared joins. Raw270 workers stay byte-preserved in authenticated GitHub artifacts; Pages gets bounded derived metrics plus original manifest. TASK010G budget refinement sequences independent VM phases to preserve authenticated provider capacity. Runtime and coverage qualification remain pending.

TASK010G rate budget: exact provider403/429 reset/Retry-After may boundedly wait outside timings, with retained headers, same route/source and no DB retries; frozen accumulated3700s/3repeats/maxparallel6.

TASK-ISO-010F approved genuine1/2/3 fault implementation; sql_audit newprefix ownership, root call/evidence join. TASK011 clarified browser/producer joins recorded. Every runtime gate remains pending.

## Fresh real baseline6ad, run37077856823

Exact SHA6ad4741a713ac3376868aef146ed60a199e97b93,
https://github.com/managedcode/KeyLoad/actions/runs/37077856823. Full build,
formatter, governance,118 analyzer cases and recovery passed; unit/scalar/native
RF3/comparison qualification is not complete. Local copied log is diagnostic
input only at /private/tmp/keyload-baseline-37077856823.log.

- [ ] OversizedCompiledFramePersistsItsFailureAndAdvancesTheRaftApplyPosition: compiled journal frame limit failure at TransactionProtocolTests.cs70; concurrent owning WAL framing repair must preserve exact limit/receipt/apply contracts.
- [ ] AcWal001RealBinaryPayloadUsesFewerEncodedBytesThanEquivalentRepresentativeJson: binary payload not smaller than real JSON; concurrent WAL owner has source repairs, awaiting exact-SHA actual unit gate.
- [ ] AcSeries012FollowerRestartRetainsLatestAndWindowValuesOnEveryReplica: committed.IsSuccess false at TimeSeriesRf3ReplicaRestartTests.cs43; actual source package/recovery owner repair pending genuine RF3, no weakened assertion.
- [ ] AspireTimeseriesProfileEmitsOracleReportAndProtectsForeignSchema: native Timescale persistenceGuarantee omitted required single-node; exact wording/source owner repair pending. Native TimeSeries must move to separate isolated target jobs, not silently disappear from qualification.
- [ ] AspireRunsIdenticalScenariosAgainstRealRf3AndExternalEngines: stream duplicate returns actual KeyLoad:RevisionConflict, old assertion KeyLoad:Conflict; current source already has exact raw RevisionConflict expectation. Whole multi-engine performance invocation is superseded by270 isolated native cells; its relevant SDK/PG/Neo semantic regressions are explicitly joined after selected PointRead timing.

TASK010 infrastructure source now joins immutable image-native roundtrip, actual
current-job capture, exact model/workload gates,27preflight,108CRUD,162specialized,
authenticated raw collector and complete bounded aggregator. Common setup/teardown
are colocated native composite artifacts under workflows/Features/BenchmarkComparisons
with policy before implementation. ci.yml remains under400lines; no unrelated gate
is waived. Source-only governance passes25projects/4modules. Required GitHub remains
pending; source compilation is a development check only.

| Task | AC / owner / tier | Permission / dependencies | Ordered artifacts / verification / join |
|---|---|---|---|
| TASK-ISO-012P | ISO007/008/009; gates_audit capable trust-boundary worker | ONLY NEW site-isolated-github-* and SiteIsolatedGitHub* prefixes; approved written contract after010G/011 source; root shared integration | Tests-first genuine archive/metadata, bounded capture/selection, exact BCL277-file preparation, immutable receipts/freshness; source review then actual native full site/browser80/70/90. Pending |
| TASK-ISO-012PJ | ISO007/008/009; root strongest integration | Shared before/after hooks, source/coverage/workflow/env joins, publication receipt | Preserve legacy12-file gates independently; capture2 isolated ZIPs, verified277 inputs, complete site suite, both final source/identity freshness checks, actual Pages/live evidence. Pending |
| TASK-ISO-012PB | ISO007/008/009; gates_audit inherited capable infrastructure worker | ONLY NEW workflows/Features/BenchmarkComparisons/BuildIsolatedSite/AGENTS.md and action.yml, approved012P contract; root owns calling workflow | Move the existing bounded12-file final-build checks intact into one composite; add277-file native before/after verification, original isolated manifest comparison and separate publication field. Static review then actual Pages suite/provider proof. Pending |
| TASK-ISO-015U | ISO003/006 and HOST007; foundation_review inherited capable worker | ONLY ComparisonHostCleanupTests.cs after read-only cause/ADR043 approval | Tighten exact unusable-endpoint cardinality and all-failed/setup/zero-sample oracle; preserve safe cleanup/secret checks. Root review then full actual Unit and required GitHub gates. Pending |

TASK011 manual exception: deterministic source mutation precisely during the
sequential producer has no legitimate native observable synchronization point.
Review both bounded no-follow manifest reads and equality check; real corruption,
positive270 raw data and post-suite/final unchanged-file gates stay automated.
Do not add a test-only production hook, fabricated positive or timing race.

Native development compilation at /private/tmp/keyload-isolated-source-build-native.log
completed with88 source diagnostics; these are not qualification/test results.
010F owns TimeProvider/catch/disposal corrections;011W owns named-key/style fixes.
Some exact diagnostics were already corrected by concurrent source owner; fresh
reads precede all joins. Required GitHub tests/coverage/runtime remain pending.

Fresh historical source c10c48e40d8a9b452c9d131da549cab1e336692d,
https://github.com/managedcode/KeyLoad/actions/runs/37079707413: Linux full build,
formatter/governance/analyzer, unit, scalar unit, process recovery and native RF3
jobs are successful. Historical comparison job still failed; source407619362
repairs its stream cancellation expectation and awaits its actual run37080578096.
Neither source contains this new isolated cohort. Earlier WAL/TimeSeries unit/RF3
failure entries retain chronology and are superseded only for that exact c10 SHA;
new270/image/Pages native qualification remains pending.

TASK010 native teardown join now preserves an already failing public assertion
while attempting every independent raw/log/stop/data/receipt stage. An owned
teardown.json records only fixed failure stages and a primaryFailure flag; cleanup
failure on an otherwise successful case still fails. The real fault/current-job
flows exercise ordinary cleanup; unforced simultaneous filesystem/log shutdown
faults have explicit source-control-flow review evidence rather than test doubles.

TASK-ISO-TS001 package identity repair (AC-ISO-006): authored a real loaded-assembly
versus central NuGet pin report regression before replacing stale10.0.0 metadata
with the actual library informational version. No dependency implementation or
release is modified; package10.0.3 delivery belongs to the existing owning repair.
New comparison source/test requires exact-SHA GitHub execution. Missing assembly
version fails rather than inventing a value.

TASK-ISO-011R (AC-ISO-008/009) is a bounded native responsive follow-up: worker
owns only isolated-view and new SiteIsolated layout assertions plus standalone
test call. Root owns scoped CSS using existing tokens. Actual270 Chrome evidence
must retain9 rows at1440/768/390/320 widths, confine620px-minimum table overflow
inside a labelled keyboard-focusable scroll region and wrap long provenance.
Tests precede the container/CSS changes; no new visual or measurement contract.

## Foundation checkpoint review

TASK-ISO-011V reviews the stopped compact producer, loader, view and genuine
site tests read-only; TASK-ISO-013V reviews selected native topology, teardown,
image import and workflow bindings read-only. Root integrates concrete findings
before the first source checkpoint. The active TASK-ISO-012P publication prefix
is excluded until its own contract implementation is complete; unrelated owning
WAL and TimeSeries changes remain visible and separate.

Exact historical407619362db859e14d7a678d4441b8d4a77862e4,
https://github.com/managedcode/KeyLoad/actions/runs/37080578096 completed with
successful Linux verify, analyzer and actual Docker RF3 jobs. Legacy comparison
failed its stale case-cardinality assertion (72 expected,96 actual); the owning
source07ca0e8074a0c29cd5b1a38c558b366834f203a0 corrects the explicit eight-scenario
matrix. No isolated270 measurements are qualified by these historical runs.

TASK-ISO-011V found a rejection-fixture path outside the protected site source
tree. Root corrected sourceChild to the actual canonical site root, preserving
the production boundary and expected rejection. Native GitHub execution remains
mandatory; source correction alone is not a passing test.

TASK-ISO-015U implementation routing was unavailable: the platform returned
`agent thread limit reached` when resuming the approved reviewer with the bounded
test-only write task. Root implements the single-file correction after completed
read-only cause analysis. The fixture has six genuine clients and ten scenarios
at one repetition, so exact expected cardinality is60; every unusable native
endpoint must report failed/setup/null measurement/zero samples. This cannot
weaken the independent isolated setup-precedence regression.

Both read-only reviews are complete with the fixture correction joined. Root
closed the remaining unbounded implicit-disposal risk under AC-ISO-006: explicit
capture/application disposal now runs as independently attempted30s teardown
stages and observes late faults. No primary assertion is overwritten afterward.
Fresh native development compilation and then exact-source GitHub remain the
join gates; the new intensive/runtime/publication receipts remain absent.

The final foundation ComparisonTests development compilation passed with zero
warnings/errors (36.78s), retained at
/private/tmp/keyload-isolated-checkpoint-native-build-r3.log. Static governance
and syntax checks of51 staged JavaScript modules passed. These are source checks,
not native test/runtime qualification. The stopped foundation contains328 scoped
paths; the active publisher and foreign owning work remain excluded from this
coherent checkpoint. Next required gate is actual GitHub source qualification.


## Native1978/63ac and publisher source join

Foundation1978d0af9453dd1b2c93be074daac6408f46da84 was pushed with328 scoped
paths. Authentic run37082268730 failed1/1405 Unit cases at the stale cleanup
unsupported assertion;118 analyzer cases and136 process-recovery cases passed,
without skips. RF3 failed1/63 after intentional kill/restart because native Aspire
health wait failed before the restarting resource became healthy. Source63ac
includes the native WaitOnResourceUnavailable correction; actual run37082449440
passes63/63 RF3 without skips, but full build fails old publisher Receipt IDE0032/
IDE0290. Fresh source corrects these without suppression. The exact durable
record is docs/implementation/isolated-comparison-source-qualification.json.
Neither run started images/preflight/intensive aggregation; there are no new
performance or publication receipts to claim.

TASK012P and012PB are source-complete after independent reviews. Root joins
mandatory before/after BCL preparation, immutable original receipt bytes, exact
39-source/31-critical native inventory, closed49-file executed dependency hash
comparison, separate actual measured-source checkouts, bounded final composite,
and both evidence freshness checks before Pages. A genuine Node/V8 TUnit
regression rejects unregistered legacy and new publisher sources. SiteTests
native development compilation passes0warnings/0errors10.33s; governance/YAML/
Bash/whitespace pass. Full Unit development compilation was blocked solely by
concurrently untracked CacheMemoryBudget.cs KLD0024; those foreign files are not
part of this checkpoint. Actual exact-source GitHub remains the qualification.

- [x] Source TASK012P worker prefix and native27 authored cases reviewed.
- [x] Source TASK012PB composite and preserved legacy12-file gates reviewed.
- [x] Root source hooks/inventory/environment/checkouts/final builder/freshness joined.
- [x] Tightened TASK015U real cleanup regression after authentic failing test.
- [ ] Complete corrected exact-SHA required CI, images and27 native preflights.
- [ ] Complete270 measured cells and authenticated aggregation.
- [ ] Qualify full site/native80/70/90/Chrome/no-skip and actual Pages/live metrics.
- [ ] Isolated TimeSeries native cohort and matched backend coverage.


## Corrected native baseline cf630

Run37084177131, sourcecf630751e0f24e4d8183e55510add9c7207e377f, passes
verify111092414086: complete Release/format/governance,118 analyzer tests,1407
normal units,1407 scalar units and136 recovery tests, zero skips. RF3 job
111092414223 passes63/63, zero skips. Common image job111093924880 passes native
image export/import1, actual authenticated job1, composition models77, historical
TimeSeries workload3 and package-version1, all without skips. The27 native
preflights now execute on distinct selected-engine runners. No complete intensive
raw cohort, numeric backend coverage, site/provider/live result is yet qualified.

## Genuine cf630 preflight failures and corrective graph

- [ ] SelectedNativeCaseRunsCommonIntensiveWorkload / keyload-n1-point-read,
  job111094622972: five10000-attempt repetitions hit ResourceExhausted. The actual
  default HTTP principal admission is8 while this profile creates16 clients;
  native bounded admission receipt and per-node diagnostic logs are needed.
- [ ] SelectedNativeCaseRunsCommonIntensiveWorkload / keyload-n2-point-read,
  job111094622918: first repetition has5336 ResourceExhausted and73 OwnershipLost;
  later warmups fail ResourceExhausted. Diagnose ownership separately from limits.
- [ ] SelectedNativeCaseRunsCommonIntensiveWorkload / keyload-n3-point-read,
  job111094622936: native setup OwnershipLost, zero measured samples. Per-node logs
  were not retained by the runner-only logger; no invented root cause.
- [ ] SelectedNativeCaseRunsCommonIntensiveWorkload / redis-n2-point-read,
  job111094622945: native setup RedisReplicaPrimaryIdentityMismatch, zero samples.
- [ ] SelectedNativeCaseRunsCommonIntensiveWorkload / redis-n3-point-read,
  job111094623016: same strict native replication identity assertion, zero samples.

| Task | AC / owner / permissions | Dependencies / artifacts / join |
|---|---|---|
|TASK-ISO-016R|ISO003/005; gates_audit read-only Redis analysis|Authentic logs/artifacts and exact source; terminal diagnosis then root-approved fix packet; no guessed host identity relaxation.|
|TASK-ISO-016K|ISO003/004/005; existing publisher_archive_review capable read-only replica analysis|Authentic KeyLoad raw/QA; root separately owns admission/diagnostics; identify missing facts/source defects; no hidden retry/quorum weakening.|
|TASK-ISO-016H|ISO003/005/006; root owns bounded benchmark HTTP configuration and native logs/admission receipts|Actual c16/default8 contradiction; approved ADR056 refinement and source/native regressions before writes; retain failures and every real job.|
|TASK-ISO-016R-F|ISO003/005; gates_audit owns only RedisResources/Bootstrap/TopologyRedisTests, bounded source writes|016R terminal diagnosis and root-approved ADR056 exact native hostname contract; tests first, root full diff review, actual GitHub 1/2/3 preflight and complete CRUD proof.|
|TASK-ISO-016K-F|REP051/ISO004/005; publisher_archive_review owns exact replication gate files and real recovery tests in ADR007|K2 lock/call-site analysis terminal; log-owned metadata gate, tests first; root full diff review, no local qualification, same-SHA recovery/RF3/intensive join.|
|TASK-ISO-016M-F|ISO003/005; gates_audit owns five Mongo/Kurrent source/model paths in ADR056|016M actual parser failure/source DNS contradiction; approved final-promise and canonical native-host contract; root diff and authentic1/2/3/full-cohort proof.|
|TASK-ISO-016N-F|ISO003/005; build_action_review owns exact Neo4j target/protocol/native test packet in ADR056|016N exact pinned upstream status mapper and foreign constraint ownership review terminal; root-approved strict202/400/nativecode + actual CREATE-success ownership; genuine regression/fullcohort join.|

These failures block the complete270 intensive cohort, site refresh and dependent
TimeSeries native implementation. The separately approved pinned-image feasibility
stage is independent. PostgreSQL preflight1/2/3 and Redis1 pass at this source;
partial native results never count as a completed cohort.

Final cf630 native baseline:27 preflight jobs complete with15 successes and12
failures. Neo4j Community2/3 are explicit unsupported proof jobs. Add individually
tracked failed cases to the same corrective graph; aggregate111097809329 is skipped
and no full270 cohort exists:

- [ ] MongoDB n1 job111094622975: actual bootstrap classic script SyntaxError at
  top-level await; no runner/JSON. Fix retained final native evaluator promise.
- [ ] MongoDB n2 job111094622970: same actual parser failure, no measurements.
- [ ] MongoDB n3 job111094622962: same actual parser failure, no measurements.
- [ ] KurrentDB n1 job111094624150: setup:GossipEndpointIdentityMismatch; native
  advertised hostname differs from actual Aspire endpoint. Keep strict identity.
- [ ] KurrentDB n2 job111094624217: same source address contradiction; zero samples.
- [ ] KurrentDB n3 job111094624589: same source address contradiction; zero samples.
- [ ] Neo4j n1 job111094623024:50000 successful measured reads, but required
  duplicate-constraint regression rejects HTTP!=202. Actual status/body not retained;
  source error-response and foreign constraint cleanup ownership need repair/proof.

TASK016H/016R-F source is authored; combined ComparisonTests development build
passes0warnings/0errors30.19s. Runtime remains unqualified until every failed case
and original required suites pass at repaired source. TASK016K-F fixes the proved
metadata race independently from unresolved historical OwnershipLost attribution.

TASK016H/016R-F/016M-F/016N-F/016K-F source review is complete. The full solution
development build passed0warnings/0errors29.41s before the final Neo4j success-envelope
refinement. That final scoped build reports zero owned diagnostics and five errors
in a concurrently introduced, unowned IsolatedResourceLogCaptureStopTests.cs.
That file and its shared logger/teardown/doc refinements remain outside this
scoped source checkpoint. Static governance and whitespace validation pass.
The scoped commit retains the earlier owned logger/teardown version and every
foreign working-tree change; only genuine exact-source GitHub can qualify it.
No local tests or runtime qualifications ran. All12 original failures remain
tracked until native repaired-source results exist.

- [x] Root-reviewed strict Neo4j native errors and malformed202 success envelopes.
- [x] Root-reviewed Redis native host, Mongo final promise and Kurrent native hosts.
- [x] Root-reviewed physical log-owned checkpoint publication and recovery cases.
- [x] Bounded isolated admission and native per-member/log receipts source joined.
- [ ] Repaired exact-source Release/format/governance/normal/scalar/recovery/RF3.
- [ ] Repaired native27 preflights and complete270 original intensive cell cohort.

Actual source890 run37086903499/job111100389518 passes solution Release build but
fails the formatter at PinnedImageProcess.cs109..110: six WHITESPACE diagnostics
on the compact record initializer. Root applies only the required multiline
formatting; no statement/value/lifecycle change. Recovery executes independently
and passes, but skipped unit/scalar/governance/image/cohort steps do not pass.
The pending ffa8 repaired-source run37087767502 is not a qualified baseline.
Retain this exact failed log and repeat complete gates at the formatting repair SHA.

- [ ] Verify EditorConfig: actual890 WHITESPACE failure; source fix authored,
  corrected exact-source GitHub formatter gate pending.

- TASK-ISO-013V-STOP-R41 [AC-ISO-006]: root owns shared logger/teardown lifetime repair; Luna owns only NEW IsolatedResourceLogCaptureStopTests.cs from the approved ADR packet. Start: R38 finding joined; tests first. Disjoint new test source only, no local qualification/Git/CI/config/doubles. Artifacts: real-Aspire stop/output/disposal caller-flow regression; root full source review/gates and exact-SHA native CI. State: implementation pending; unforced delayed native fault remains documented review exception.

- TASK-ISO-013V-STOP-SOURCE-R49 [AC-ISO-006]: accepted ADR-056 preserving repair after actual full formatter CA2000/CA1848 findings. Luna sole test writer owns StopTests.cs and two explicitly named optional new fixture/support files from the ADR. Start: current regression and R44 independent review exist; source diagnostic reproduction is retained at /private/tmp/keyload-r48-integrated-development-format.log. Output: proper unconditional disposal/constructor ownership and cached native log delegate with all original assertions and bounded task observation. No production/shared/config/docs/Git/CI/local execution. Root reviews every diff and current source gates, then exact-SHA GitHub. State: approved, unqualified. R44 production lifetime findings remain closed; diagnostic repair is a separate source gate.
## Native b474 baseline and continued source repair

Run37087909605 atb47409e73427c1394ccf705f398a541f2e7cb08b passes complete
Release/formatter/governance, analyzer118/118, normal/scalar1407/1407,
recovery138/138 and RF363/63, all zero skips. The original ffa8 RF3 survivor
windows failure remains unresolved timing-sensitive routing evidence; a passing
repeat is not a causal repair. Native image job111104670277 passes its genuine
image/job/composition probes. The27 preflights are independently executing;
their failed jobs prevent270 allocation and publication.

- [ ] KeyLoad n1 job111105163526: all50000 timed reads succeed, then the real
  SDK/MCP document oracle expects original property order. TASK-ISO-019J root
  preserves unsorted input and adds exact independent canonical stored strings;
  source correction authored, native complete public regression still pending.
- [ ] KeyLoad n2 job111105163562: first10000 attempts4394success/5606OwnershipLost,
  later four repetitions fail before timing. Native QuorumRead rejection and
  membership signed no-majority replies retained; precise quota/cause still open.
- [ ] KeyLoad n3 job111105163601: first10000 attempts5918success/4082ResourceExhausted,
  later four repetitions fail before timing. Native QuorumRead rejection retained;
  separate read/critical replay-pool cause requires source and native proof.

| Task | AC / owner / permission | Start, artifacts, verification and join |
|---|---|---|
|TASK-ISO-CI-018|ISO003/005/007; gates_audit read-only capable|Actual b474 job/archive logs and exact hashes, topology-specific failures and approved source repair proposal. No retries, local qualification or test relaxation.|
|TASK-ISO-019J|ISO004/005; root serialized existing public regression files|Accepted canonical JSON contract and native n1 failing regression; two source files plus docs. Scoped source gates then genuine same-SHA1/2/3 full public/native checks. Pending qualification.|

- TASK-ISO-013V-SUBSCRIBER-R59 [AC-ISO-006]: approved ADR-056 R57 real subscriber original-task ownership finding. Luna owns only StopTests.cs, StopSupport.cs and one optional new IsolatedResourceLogSubscriberScope.cs. Register actual MoveNext/publish tasks before waits, retain synchronous/asynchronous primary publication failures, cancel the real subscriber lifetime, join/observe originals and defer enumerator disposal until moves settle, with independent finite cleanup bounds and fault observers. No fake service/enumerator, production/fixture/shared/config/docs/Git/CI or local execution. Root joins every diff and independent final review/source gates; original simultaneous-fault manual exception remains explicit, genuine GitHub proof pending.


| Task | REQ/AC / owner / tier / permissions | Dependencies and join |
|---|---|---|---|
|TASK-ISO-020RM|ISO003/005/006; build_action_review inherited capable tier; bounded diagnostic/test source writes only|Root accepted ADR056 packet above; preserve exact failed source/native archives. Tests first, source build/static review, root complete diff, exact-SHA native1/2/3. Root owns Git/docs/CI; no behavior fix before native predicate evidence.|

- [x] Four original failed native archives verified; strict source boundary reviewed.
- [x] Accept diagnostic-only contract before delegated writes.
- [ ] Diagnostic source/tests reviewed and built; genuine native predicates retained.
- [ ] Proven behavior repair separately approved and all native gates green.


| Task | REQ/AC / owner / permissions | Dependencies and join |
|---|---|---|---|
|TASK-ISO-021P|REP006/ISO003/004/005; gates_audit inherited high capability; bounded Replication-only source/new real-node tests|Accepted ADR036 packet; tests first, no shared fixture mutation without approval. Root reviews full diff/source gates; exact-SHA normal/scalar/recovery/RF3/native1/2/3 required.|
|TASK-ISO-021T|same AC; root serialized Orleans crypto/membership/enum/profile/diagnostic integration|Parallel disjoint Replication worker; keep shared Abstractions and foreign sources untouched. Genuine signed security tests, strict pool evidence and full native join.|

- [x] Native failure/source analysis; approve strict application/control separation.
- [x] Replication source and acceptance-derived real-node tests reviewed; exact-SHA execution pending.
- [x] Strict signed classification, membership join and bounded diagnostics reviewed; signed max-index boundary repaired.
- [ ] Actual full source/recovery/RF3/native1/2/3 and all270 gates green.
- [ ] Freeze and qualify actual native control execution under sustained pressure.


TASK-ISO-021P test instrumentation refinement: NEW RecoveryTests ClusterReplication
ReadRoundStoredNode.cs owns distinct genuine ZoneTree/replica directories,
DatabaseEngine/DurableReplicaLog/ReplicaMaterializer/ReplicaConsensus with original
timing. NEW ReadRoundProtocolTransport.cs maps a fixed immutable set of actual
stored nodes; InvokeAsync records method/source/destination then awaits actual
target Consensus.HandleAsync with original payload/token, returning its actual
result/error. No manufactured reply/fault/delay/retry/dropped target or service
double. NEW ReadRoundStoredCluster.cs attaches per-node protocol links, waits
bounded real election/readiness and owns orderly cleanup. NEW ReadRoundProtocolTests.cs
and ReadRoundGuardTests.cs cover actual RF1 local and RF2/RF3 local/remote read
purpose, committed cuts, receiver guard-before-mutation and caller cancellation.
This is real stored protocol integration instrumentation, not native Orleans
transport, production topology or fault/performance qualification. Separate
genuine signed admission tests and Docker/Aspire RF3 remain mandatory joins.


TASK-ISO-021T source integration details: ReplicaEnvelopeAuthenticator adds an
optional fifth ILogger<ReplicaEnvelopeAuthenticator> constructor argument; all
source callers remain coherent and native DI supplies the actual logger. This
is a homogeneous source/ABI rollout, not a retained legacy constructor shim.
The exact authenticated quota snapshot and fixed rate state are source-owned
ReplicaReplayAdmissionFailure/Diagnostics; one numeric configuration record
retains actual node pool capacities, and at most one rejection record per
configured sender/pool/30s retains counts/expiry/suppressions. LoggerFactory
provider AggregateException cannot replace the original authoritative quota
denial; that defensive provider-failure edge needs real provider evidence,
not a mocked logging service. Native retained configuration/denial logs remain
a required join; resource-model tests alone do not prove runtime configuration.

IsolatedKeyLoadReplayProfile explicitly sets all four capacities on each actual
benchmark resource via existing IsolatedKeyLoadAdmission.Apply. New genuine
AppHost model cases verify1/2/3 configs and absent production overrides; genuine
signed security cases verify pool isolation/malformed/replay/MAC/sender edges,
and actual LoggerFactory/provider capture verifies closed numeric/privacy/rate
evidence. No public status/worker schema or dependency changed.


Final b474/run37087909605 native baseline:27 completed preflights,17success/
10failure. Failures are KeyLoad1/2/3, Redis2/3, Mongo2/3, KurrentDB1/2/3; all270
allocation and aggregation skipped by the original strict dependency. Kurrent
original ZIP digests: n1 artifact11261522797/job111105164606 SHA256
9896cff17968bc353c0d6db95e911f53b6bc967b75f69527b3010b7da25cb55b;
n2 artifact11262241672/job111105164603 SHA256
05a753db8f6191788a7fe1138a7733f637cbab73847a0fd42616a1b78d146bfd;
n3 artifact11262496282/job111105165089 SHA256
bbef699135f8aa8aa2a7afff59e4fbfee99d61b1c013aa6b399aa79642f021f8.
All fail setup:KurrentGossipMembershipMismatch with zero timed samples and clean
owned teardown. Native membership logs show full26.1.2.3778 build identity;
the exact gossip predicate requires pinned source/native response evidence.

- [ ] KurrentDB n1 job111105164606: exact native gossip membership failure.
- [ ] KurrentDB n2 job111105164603: exact native gossip membership failure.
- [ ] KurrentDB n3 job111105165089: exact native gossip membership failure.

TASK-ISO-022K read-only source/native diagnosis follows020RM completion; same
build_action_review owns analysis only, preserving original ZIPs and strict
version/endpoint/member/role/ack assertions. Root freezes any repair separately.


| Task | REQ/AC / owner / permissions | Dependencies and join |
|---|---|---|
|TASK-ISO-022K|ISO003/005/006; build_action_review bounded constant/comparison/identity test writes|Original3 ZIP hashes/native build + exact official commit prove rejection. Tests first, retain three-part image profile and four-part raw gossip. Root review/source gates, real native1/2/3 HTTP/member/copies then full270 required; other failed predicates not assumed absent.|


TASK-ISO-020RM root process-ownership join: after the worker's terminal source
packet, root serially refines MongoBootstrapDiagnosticTests only. Cleanup
independently collects child cancellation/reaping and both original stdout/
stderr tasks under finite bounds, observes unsettled originals, and preserves
the initiating failure first alongside cleanup failures. This is real owned
Node process lifecycle coverage for pure projection inputs; native Mongo
readiness/cause remains the exact-SHA real-container gate. No foreign logger/
teardown/subscriber source is part of this checkpoint.

## TASK-ISO-023 coherent owned-source checkpoint

Root verifies an immutable HEAD-based projection of the83 approved owned files;
this is a source verification artifact, not a new implementation checkout. All
implementation remains in the shared checkout. Foreign cache/serialization/
checkpoint/logger-lifetime edits remain visible and outside the scoped commit.
The first projected complete Release build reports0warnings/four owned errors:

- [x] MongoBootstrapDiagnosticTests CollectAsync CA1031: observe and collect the
  actual task fault/cancellation after bounded await; no broad swallowed catch.
- [x] ReplicaControlBarrierSecurityTests lines21/22/23 TUnitAssertions0005:
  assert reflected runtime enum values instead of constant expression assertions.
- [x] Complete owned-source Release build0/0, full formatter exit0 and governance pass.
- [ ] Stage only reviewed owned bytes/partial documentation, commit/push main.
- [ ] Exact new SHA GitHub full normal/scalar/recovery/RF3 and27 preflights.
- [ ] Inspect strict Redis/Mongo native predicate diagnostics; freeze proven
  repairs before further behavior writes, then complete270 and publication.

Every source-only pass remains distinct from genuine GitHub qualification.


| Task | REQ/AC / owner / permissions | Dependencies and join |
|---|---|---|
|TASK-ISO-023L|BC055/ISO006L; gates_audit inherited capable; three frozen log-buffer/parser/new-test files only|Accepted ADR056 before writes; tests first, bounded numeric/original-line collection; root full diff/source gates and genuine same-SHA native logs. No foreign capture/teardown/lifetime writes.|

- [x] Closed original diagnostic parser/13-slot retention:51 acceptance cases authored; actual execution pending.
- [x] Root complete diff/source build/format/governance (source-only).
- [ ] Exact-SHA native1/2/3 config/quota original byte retention and full cohort.

The coherent Release retry succeeds0warnings/0errors33.86s, including the complete
source/test solution. Complete formatter then identifies eight whitespace
locations in four owned files (ReplayAdmissionDiagnostics, Mongo diagnostic
process cleanup, ReadRoundStoredNode initializer and ReplayDiagnosticsTests).
Root applies only scoped canonical whitespace formatting; no semantics change.
The combined formatter repeat and retention source join remain pending.


Final retention-source Release join passes0warnings/0errors70.78s; static
governance passes. Worker final review requires explicit ASCII numeric tokens
and NUL/tab/non-ASCII negative cases before final freeze, within the same three
files. This source boundary hardening precedes the final formatter and commit.


| Task | REQ/AC / owner / permissions | Dependencies and join |
|---|---|---|
|TASK-ISO-023Q|BC055/ISO003/005/006/006L; root serialized ci.yml selector only|Existing filters omit new5 comparison classes. Accepted additive exact TUnit step; static source review then real same-SHA job/result counts before native27/270. All existing gates and permissions retained.|


TASK-ISO-023 final source checkpoint:89 reviewed owned files at HEAD b474,
complete Release0warnings/0errors23.29s, full dotnet format verify exit0,
governance PASS25 projects/four modules, exact primary/projection byte inventory
and owned whitespace diff checks. No local test/runtime/load was executed.
The explicit new5-class GitHub selector joins the frozen51 closed-retention
cases with replay profile and Redis/Mongo diagnostic regressions. Foreign
cache/serialization/checkpoint/logger-lifetime changes remain visible and out
of the scoped commit. Native pressure/control and separate TimeSeries runner/
adapters remain explicit unqualified dependent workstreams.


Exact2f source gates completed: run37093197474 attempt1 verify111117715444
normal1483/scalar1483/recovery164 and analyzer118, all failures/skips/cancellations/
timeouts/flaky0; native RF3 job111117715354 passes63/63. Original reports prove
TS006C30 cases each normal/scalar mode and ReadRoundGuard22/Protocol4. See
[committed-source receipt](docs/implementation/isolated-source-qualification-37093197474.json).
This completes the TS006C source/correctness join; new TS007B source, comparison
diagnostics, native27/270, TimeSeries6/30 and site remain separate pending gates.


Exact2f comparison-images job111119723225 passes. Original per-invocation log
qualifies ImageBundle1/currentJob1/resourceModels77/httpAdmission2/logBuffer1,
replayAdmission4/RedisDiagnostics22/MongoDiagnostics25/replayGrammar42/
replayRetention9 and legacyTimeSeries3/packageVersion1, all no failures/skips.
See source receipt for original log hash and distinct-invocation counts.
TASK-ISO-020RM diagnostics and TASK-ISO-023L grammar/retention source cases
are qualified; actualRedis/Mongo faults and nativeKeyLoad pressure are separate
27 preflights now running. Full270/cohort/site remain pending.


| Task | REQ/AC / owner / permission | Dependencies / artifacts / verification / join |
|---|---|---|
|TASK-ISO-026K|BC053/055,ISO004/005; root integration owner, narrow existing Messaging oracle only|Original2f RF1 stream canonical payload failure retained after50000 successful reads; ADR056 accepted before fix. Correct three returned-payload expectations, preserve unsorted requests and every assertion. Full source gates then exact-SHA real native1/2/3 SDK/MCP/SQL; no local tests. Root owns join.|
|TASK-ISO-026PG|BC052/055,ISO002/003/006; build_action_review capable read-only|Original2f PostgreSQL n3 bootstrap fails requested WAL segment already removed, n1/n2 success. Retain provider/raw/log hashes; inspect genuine native slot/retention contract before any root shared-bootstrap writes. No retry/deadline/assertion weakening.|

- [ ] 026K RF1 stream assertion: initial repair covered documents only; extend
  independent canonical expectations to stream and queue inspection/delivery.
- [ ] 026PG native PostgreSQL3 bootstrap: WAL000000010000000000000002 removed
  before pg_basebackup completed; native retention root cause/repair proof pending.


| Task | REQ/AC / owner / permissions | Dependencies and join |
|---|---|---|
|TASK-ISO-026R|BC052/055,ISO002/003/006; gates_audit inherited capable; two frozen Redis files and NEW native readiness regression only|Accepted ADR056/AC before write. Original2f link handshake failure retained; tests first, copy-before-final-proof and INFO link guard inside original60s/200ms barrier. Root joins native helper in existing per-engine job, reviews source/build/format, delivers and qualifies native1/2/3; no Git/local runtime/workflow rights.|

- [ ] 026R Redis2/3 initial sync: final strict proof ran before bounded copy
  observation; preserve exact native predicates and qualify ordered readiness.


TASK-ISO-026PG root accepted repair after read-only actual WAL diagnosis.
Root serialized shared bootstrap/resources/shell/models/native-regression join;
0/1/2 early physical slots and existing-slot recovery with native512MB retention.
Tests first, source gates then genuine1/2/3 parallel bootstrap and WAL cut proof.
No public persistence/API migration or production topology change.

- [ ] Native reserved-slot source/models/WAL-cut regression and root review.
- [ ] Exact-SHA original PostgreSQL3 bootstrap and1/2/3 slot qualification.


| Task | REQ/AC / owner / permission | Dependencies and join |
|---|---|---|
|TASK-ISO-026M|BC052/055,ISO002/003/006; gates_audit inherited capable; existing Mongo native shell auth and NEW auth regression only|Native20245 cleared-user event and13Unauthorized original2f proof; accepted ADR056 before write. Tests first, bound cached objects and renew actualauth in existingpoll. Root joins existingper-engine native selector, reviews/builds/delivers/qualifies actual1/2/3. No localruntime/Git/workflow/docs/other edits.|

- [ ] 026M Mongo2/3 stale authenticated native shell after initial-sync UUID
  replacement; renew real auth and qualify unchanged strict membership/copies.


Exact2f terminal baseline:27 native preflights finish16 job successes and11
failures, with two unavailable Neo4j Community topologies among the successes.
Actual Qdrant/Rabbit/OpenSearch1/2/3 and PostgreSQL1/2/Mongo1/Redis1 succeed.
KeyLoad1/2/3 and Kurrent1/2/3 each complete50000 intensive operations with0
failures, but later public-oracle/target-disposal errors invalidate those jobs.
Retain every failed result; full270/aggregation/site remain unqualified.

- [x] 026K all three returned Messaging payload expectations corrected in source.
- [x] 026R reviewed source ordering/link guard and genuine copy/cancel helper joined.
- [x] 026PG early0/1/2 permanent slots, bounded native setting, models and native
  WAL-switch/checkpoint/copy-cut regression authored/joined. Source build pending.
- [ ] 026KC Kurrent1/2/3 disposal after complete measurements: specific cleanup
  native error not yet visible; do not call it a proved timeout from timing alone.


| Task | REQ/AC / owner / permissions | Dependencies and join |
|---|---|---|
|TASK-ISO-026KC|BC054/055,ISO005/006; build_action_review inherited capable; two frozen existingKurrent files, NEW cleanup/diagnostic/native regressions only|Original2f all50000successfulthenproveddisposal failure; acceptedADR056 beforewrite. Testsfirst,16originalcleanupworkers under20s/30s, fullack/drain/disposal/error preservation. Rootselectorjoin/review/sourcegates/native1/2/3, no localruntime/Git/workflow/docs.|

- [x] 026KC bounded cleanup/closed diagnostics and small native oracle source complete; nine TUnit cases and Kurrent StreamAppend1/2/3 selector joined. Native execution remains pending.
- [ ] 026KO separate pre-ACK stream ownership: real same-prefix conflict and
  unknown-ACK behavior must preserve foreign data; exact contract still pending.
- [ ] 026KF genuine quorum-loss cleanup fault/drain: exact timing/ownership
  contract and native evidence pending; no fault behavior claim from source.


026 source join complete development build records0warnings/four errors:

- [x] Root selector KLD0033: else-if accumulation counted nesting5; use closed
  switch cases with identical target/PointRead conditions and original flows.
- [x] Root native slot helper CA2100: remove arbitrary SQL helper parameter;
  instantiate only the two exact constant native commands.
- [ ] Foreign ongoing ZoneTreePointCacheOwnerGateHold CA1822 and
  ZoneTreePointCacheOwnerIdentityTests IDE0005: preserved outside owned scope.
  No qualification claim until actual integrated and exact delivered source pass.


## Original current-main baseline 2ec / run37093992229

Exact source2ecbeee4d7b969129b93346d2863d50072976a68, attempt1.
Source build/format pass; normal1522/1539, recovery164/164 and analyzer118/118.
RF362/63; scalar and downstream native comparison gates skipped.
See [original receipt](docs/implementation/isolated-current-main-baseline-37093992229.json).

Each original failure remains tracked until a causal repair and exact-SHA
qualification exists. Concurrent fixture/queue repairs are outside this
comparison source scope; preserve them and join their delivered evidence.

- [x] ZoneTreePointCacheCoherenceTests.SameCutCompactionRetainsWarmEntriesButSnapshotReplacementAndReopenAreCold: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheCoherenceTests.StagedPutDeleteResetAndRejectedCompilerCannotWarmOrChangeCommittedValues: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheCoherenceTests.ChangedApplyInvalidatesAWhileUnrelatedWarmBRemains: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheFillLifetimeTests.DuplicateConcurrentFillsKeepBothCandidatesChargedAndReleaseTheLoser: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheFillLifetimeTests.PinnedLruVictimStaysChargedAndNewValueFallsBackToNative: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheReadTests.BorrowedWarmReadPreservesLogicalObserverChargeAndCapturedKeyMutationCannotRebindFill: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheReadTests.ColdOwnedReadWarmsExactKeyAndWarmOwnedReadReturnsIndependentBytes: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheReadTests.EmptyPositiveValueWarmsWhileMissingAndTombstoneRemainNativeMisses: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheCapacityTests.OversizedKeyValueAndUnavailableIndexReturnNativeBytesWithoutRetention: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheCapacityTests.TwoStoresShareTheExactPoolEntryCapAndResumeAdmissionAfterOwnerDisposal: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheCapacityTests.ClearAndDisableKeepIndexChargeUntilActualStoreDisposal: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheCapacityTests.SeventeenPinnedVictimsBoundOneAdmissionToSixteenAttempts: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheLifetimeTests.DisableDuringPinnedBorrowRetiresEntryUntilBorrowerReleases: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheLifetimeTests.ReaderAndObserverFailuresReleasePinsAndCandidatesBeforeHealthyReads: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheDeletionTests.WarmDeleteMissesNativeTwiceThenDifferentReinsertIsColdAndWarmsCorrectly: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreeCoordinatedPointCacheReadTests.ReadyReceiptIsIdempotentAndWarmOwnedReadsReturnIndependentCopies: actual TUnit Byte-to-Int32 assertion conversion. Intended path: typed byte assertion in its owning cache fixture scope; no cache-value corruption conclusion. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ZoneTreePointCacheAuthorizationTests.WarmRawPrincipalAndCredentialBytesNeverBypassExpiryOrRevocationChecks: actual fixture OpenStoreAt identity rejection. Intended path: preserve and correct fixture persisted cluster identity in its owning cache scope. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.
- [x] ClusterTests(ClusterFixture).ReplicatedAtomicBatchSurvivesLeaderContainerKillAndMinorityRejectsWrites: actual queue Receive HTTP503/UnknownWriteOutcome after leader kill; original Orleans RPC cancellation observed, deeper quorum/term/apply cause unresolved. Intended path: causal public queue/leader-loss repair and exact RF3 fault proof. Qualification: actual d45 normal/scalar or RF3 original case passes; old diagnosis is historical, and nullable-memory fixture cause is recorded below.


| Task | REQ/AC / owner / permissions | Dependencies and join |
|---|---|---|
|TASK-ISO-026-JR|BC054/055/062/064,ISO005/006,TSI004/008; gates_audit capable, read-only integration review|Frozen worker source joined by root: review only six root runner repairs and native selector against accepted contracts, actual file hashes and TUnit assertions. No code/docs/Git/build/runtime writes. Report concrete blockers or complete with reviewed inventory; root owns final source and exact-SHA qualification.|

- [x] 026-JR final root runner/native selector source review complete; seven file hashes and meaningful17-case subinventory checked, native qualification pending.
- [x] 026 source-only milestone: owned HEAD projection build/format/governance, preserved foreign tree, scoped main commit/push, exact run capture.

|TASK-ISO-026KO-R|BC054/055,ISO005/006; build_action_review capable, read-only stream ownership planning|Frozen026KC source complete. Inspect every actual TrackStream/NoStream append path and pinned native SDK conflict/unknown-ACK contract. Propose exact ownership and real same-prefix foreign-data/unknown-outcome test contract, disjoint write scopes and root joins. No edits, tests, builds, runtime or Git. Root freezes ADR056 before implementation.|

Root combined development build after the final selector join passes0warnings/0errors (21.74s). This includes current concurrent source and is not test/native qualification; an owned HEAD projection is required before the scoped milestone commit. The foreign CA1822/IDE0005 findings are absent from this build but their owning delivered-source qualification remains pending.


026/TS007B-D frozen source is delivered in main397a89c46df8caaf0cc89a51e2e1c7b9208adcc6
through the concurrent coherent checkpoint. Root verified85 exact owned blobs
and preserved3 shared documents; root did not mutate the concurrently staged
foreign scope. Both integrated/projection development builds and formatters pass,
static governance passes. Real run37097831105 attempt1 completed failure17 normal fixture cases; its
RF363/recovery164/analyzer118 pass, scalar/native comparisons are skipped.

| Task | REQ/AC / owner / permissions | Dependencies/start/artifacts/verification/join |
|---|---|---|
|TASK-ISO-027KA|BC054/055,ISO005/006,KO001..005; build_action_review inherited capable; exact ACK ledger/helper/path and NEW pure/native helper ownership in ADR056|026KO-R complete, root accepts narrow OptionA before writes. Testsfirst, unchanged native EventId/Data/NoStream/026KC. Source manifest/case map/final packet; no local tests/runtime/build/Git. Root review/native selector/in-container join/source/GitHub gates. Independent disjoint SDK adapter writes continue.|

- [x]027KA pure regressions and bounded ACK-only ownership source join reviewed; nine declared pure cases and actual same-prefix native helper authored/selector joined. Native full-target unknown/fault ownership gates remain pending.
- [ ] Real different-ID foreign protection1/2/3 and full-target four-path failure proof.
- [ ] Separate identical-ID/generation contract and genuine unknown quorum/transport proof.


## Actual397 baseline failure and nullable fixture causal repair

Run37097831105 attempt1: verify111131287728 failed17 normal cases; RF3
111131287611 and analyzer111131287699 succeed, comparison jobs skipped. Original
log /private/tmp/keyload-ci-397-verify.log preserved. All17 failures are now
ZoneTreeIdentityFile.Validate49 from fixture OpenStoreAt80; previous Byte/Int
case succeeds, but the new DefaultKeyPersistsOnReopenAndExplicitKeyPresenceIsPreserved
also fails. The ternary `signingKey is null ? null : new ReadOnlyMemory<byte>(signingKey)`
can choose nonnullable ReadOnlyMemory via its null-array implicit conversion,
then lift an empty value to nullable; an absent configured key becomes present
empty and is correctly rejected. Root fixes ONLY the null branch with explicit
nullable-memory cast. Do not weaken storage validation, key length or empty-key
negative. Existing failed genuine fixture identity regression is tests-first
proof; exact-SHA full normal/scalar/recovery/RF3 must qualify the repair.

|TASK-ISO-028F|BC055/ISO005 prerequisite,AC-CACHE015; root integration; fixture null-branch ONLY|Actual17 failures reproduced in original GitHub log, prior genuine identity test already fails. Explicit nullable null cast preserves absent/valid/empty distinctions; no provider or persistence change. Source check/build and new exact-SHA native gate required.|

- [ ] 028F exact-SHA17 original failed fixture cases and all normal/scalar gates.


|TASK-ISO-028R|BC055/ISO005,TSI004/008; gates_audit inherited capable, read-only actual397 baseline diagnosis|Real run37097831105 terminal verify failure. Preserve exact SHA/run/attempt/job/artifact original reports/digests,17 original names/causes and original counts, new36 runner subgroup versus failed full/scalar skip, actual RF3 qualification separately. No source/docs/Git/build/test/runtime/provider mutations; temporary bounded packet only. Root owns receipt/docs and causal nullable fix delivery.|


The original397 seventeen names (including DefaultKeyPersistsOnReopenAndExplicitKeyPresenceIsPreserved) now each pass in BOTH actual d45 normal/scalar reports. [Original-source proof](docs/implementation/isolated-source-qualification-37098964980.json) binds exact reports/provider digests and all passing1583/164/63/118 gates. This closes TASK-ISO-028F fixture correctness/source qualification only; native comparisons and new dirty source remain pending.


Actual d45 native baseline newly identifies two independent failures before270:
Mongo2 image import exits13/unsettled top-level await after owned registry starts,
before any database/test; Mongo3 rep2 has3 actual MongoNotPrimaryException failures
during priority takeover from mongo3 to intended higher-priority mongo1. Original
failed worker/provider archives remain failures; no retries erase measured errors.

|TASK-ISO-029R|AC-ISO006/007/008; publisher_archive_review capable read-only; image-manifest/image bundle native tests and IsolatedMongoInitiate/native tests|Inspect actual d45 original source/logs/provider receipts and primary Node/Mongo APIs. Propose exact referenced original-request deadline/drain and intended leader/native readiness proof plus real regression mapping. No source/docs/build/runtime/Git; root approves ADR056 contract before bounded writes. Distinct importer and Mongo bootstrap ownership can execute in parallel after approval.|

- [x] ISO029 original native failure receipts sealed and source causes reviewed; terminal durable receipt retains24successful jobs/3failures and full270allocated0.
- [ ] Original HTTP request deadline/event-loop lifetime repair and real fresh-registry regression.
- [ ] Mongo intended-priority leader native readiness repair and real member regression.
- [ ] Full source gates/delivery and new exact-SHA27/270/site evidence; failed old cohort stays failed.


## TASK-ISO-030K accepted native canonical cleanup join

Brainstorm and acceptance remain the root isolated-comparisons documents. Original d45 preflight now completes24 job successes/3 failures; full270 was not allocated. Kurrent2 original native timed samples all pass but actual55378 tombstones exhaust20s at30125ACKs. The source baseline gates all pass and the immutable original failure remains.

| Task | REQ/AC, owner, permission | Dependency, artifacts and join |
|---|---|---|
|TASK-ISO-030K-S|BC054/055,ISO005/006,KC030001/002/003; cost-efficient capable worker; KurrentConstants cleanup bounds and NEW IsolatedKurrentVolumeRegression* only|AcceptedADR056 before writes, native assertions first. No local tests/build/runtime/Git/docs/workflow/shared-selector/other source edits. Source/hash packet, meaningful actual native assertions. Root disjoint selector join and independent full source/native qualification.|
|TASK-ISO-030K-J|Same criteria; root sole integration owner|After frozen worker packet review, add full-volume helper to existing StreamAppend selection, source build/format/governance, coherent main delivery, actual GitHub native1/2/3 and complete270 artifact chain. No prior failure overwritten.|

- [x] Original source baseline and exact native2 teardown diagnosis recorded.
- [x]120s/180s untimed bounds and900s independent canonical fixture contract accepted.
- [x] Native full-volume source assertions, bound update, root and independent030K-IR review, selector and four pure-case workflow join. Native execution remains pending.
- [ ] Delivered-SHA full Linux source gates plus native1/2/3 complete-volume and full original workloads.
- [ ]026KF native cancellation/quorum-loss plus parent kill/reap/drain fault gate; existing deferred observations are not complete settlement.
- [ ] Authenticated complete270 aggregation, site coverage/browser and publication.


| Task | REQ/AC / owner / permission | Dependencies / artifacts / join |
|---|---|---|
|TASK-ISO-031M-S|BC052/055,ISO002/003/006/007,MR031001..005; capable high-reasoning native BSON/process worker; four explicit AppHost files and NEW Mongo-prefixed pure/native tests|029R exact pinned source discovery and acceptedADR056. Testsfirst; no localruntime/build/tests/Git/docs/workflow/shared-selector. Root gets frozen hashes/evidence/test map; owns source review/selector/delivery/full GitHubnative1/2/3.|

- [x]031M exact native Long/config field semantics and intended-primary/two-round contract approved.
- [ ]031M domain/native assertions and closed helper/mount source; root independent review/source gates/selector.
- [ ]031M delivered-SHA native1/2/3/domain/election and complete270 cohort.
- [ ] Native Mongo cancellation/process-boundary/follow-up remains pending until real original child/streams completion is proven.

|TASK-ISO-030K-IR|KC030001..003; current_ci_audit inherited capable, read-only current six-file volume/timeout source and original cleanup ownership join|Worker source frozen after root delegate-signature/token correction; independently review actual ACKs, original reads/tombstones/foreign ownership, bounded16 workers, complete original task joins and fatal/primary/cleanup precedence. Temporary review/current-hash packet only, no source/Git/build/test/native/provider edits. Root selector and exact-SHA native1/2/3 remain required.|

|TASK-ISO-026KF-R|KC030003/ISO006 fault dependency; cost-efficient capable read-only researcher, original Kurrent cleanup/process/native test owners|Parallel to source030K/031M integration, inspect original cancellation/disposal/fatal/late task lifetime and existing native process fault patterns. Bounded temporary proposal/current-hash manifest, actual native acceptance/test mapping and smallest owning-source fix; no source/Git/build/tests/native/provider changes. Root freezes required ADR contract before future writes; proposed or observed-late tasks never satisfy a complete native fault gate.|

|TASK-ISO-031M-IR|MR031001..005/ISO006/007; current_ci_audit inherited capable, read-only intended leader/module/actual child ownership source|Start from031M frozen worker packet; verify exact native BSON, same-source two rounds, genuine stepdown/domain oracles, pinned explicit name/alias identity and original process/readers/cleanup contract. Temporary bounded review/hash manifest only, no source/Git/build/tests/provider/native writes. Root sole shared selector/model/workflow/delivery owner; incomplete native fault paths stay pending.|

|TASK-ISO-031M-C1|MR031003/004/ISO006/007; cost-efficient capable compiler-refinement worker; NEW ResourceTests/IdentityTests/TaskFailureTests/Topology only|Root98-file development build finds12 exact source/style/nullability gates. Frozen worker implementation remains historical; replace literal machine keys with independent named test constants, collection/null-coalescing style, awaited actual CTS CancelAsync and nonnull actual exception guard; remove unnecessary using. No assertion weakening, native algorithm/process/helper/schema change, local build/tests/runtime/Git/docs. Root reviews superseding hashes, full development source gates and exact-SHA CI.|
