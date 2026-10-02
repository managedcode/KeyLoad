# KeyLoad site design execution plan

Current as-of evidence: exact H source `6a82c86d0113270335368bbfbff0080ea1c1800a` passed complete GitHub qualification and both automatic Pages triggers. [Canonical receipts](docs/implementation/site-design.json) retain every earlier failed attempt. Historical pending/source-only descriptions below record their original stage; current website gates are resolved by the closure section. Strongest TASK-SITE-REVIEW-007 integrated review is COMPLETE.

[Brainstorm](site-design.brainstorm.md) → [acceptance](site-design.acceptance.md) → [BenchmarkComparisons](docs/Features/BenchmarkComparisons.md) → [ADR-040](docs/ADR/ADR-040-static-site-threejs-evidence.md). Owner-approved design/Three.js scope; strongest TASK-SITE-PLAN-001 COMPLETE. All existing BC/product/evidence rules remain mandatory.

## Frozen visual and technical direction

Editorial warm-paper/near-black/forest with restrained lime, high-contrast serif display against system sans/mono UI, thin separators, generous spacing. Hero: “Documents. Events. Graphs. One data engine.” plus visible early-development/cluster-qualification status; Explore benchmarks and Read architecture. Conceptual RF3 axonometric beside hero. Six capabilities, then a readable evidence workspace with provenance/control rail and chart/table, methodology, engine guarantees and source/docs links. No external fonts/CDN/backend or framework migration.

Root contracts define every DOM hook before workers start. Mounts are independent: `mountBenchmarkLab({ root, catalogUrl }) -> { dispose }`; `mountClusterScene({ host, motionButton }) -> Promise<{ dispose, setMotionEnabled }>`. Scene import/init failure never blocks lab/content. Preserve numerical semantics; schema3 remains separate pending scope.

## Bounded graph and ownership

| Task | REQ/AC | Model / exact scope | Start / verification / terminal join |
|---|---|---|---|
| TASK-SITE-PLAN-001 | BC011–018 | gpt-6-astra ultra, read-only | COMPLETE frozen packet before implementation |
| TASK-SITE-VISUAL-002 | BC011/013 | gpt-6-luna high: feature index.html, styles.css, tokens.css, assets/cluster-poster.svg | Spec/ADR/plan + DOM packet; no JS/build/docs/policy/dependency writes. Full artifacts/hashes + static source evidence; COMPLETE required |
| TASK-SITE-SCENE-003 | BC012/013/016 | gpt-6-luna high: cluster-scene.mjs, scene-geometry.mjs, scene-lifecycle.mjs, scene-observers.mjs, scene.css | Verified vendor manifest + exact root contracts; public pinned APIs only, no extra dependency. Additional observer helper preserves file limits; no public contract change. Full ownership/lifecycle/static evidence; COMPLETE required |
| TASK-SITE-TEST-004 | BC014–017 | gpt-6-luna high: tests/KeyLoad.SiteTests/Features/BenchmarkComparisons/ only | New local policy/project + frozen probe protocol; tests before corresponding evidence implementation. No local test execution. COMPLETE source packet, build/static evidence; CI join root-owned |
| TASK-SITE-EVIDENCE-005 | BC014/015/016/018 | gpt-6-luna high: measurements.mjs, measurement-loader.mjs, benchmark-lab.mjs, benchmark-chart.mjs, benchmark-profiles.mjs | Frozen validators/probe/test criteria; preserve old module arithmetic. Full code/hash/source-evidence packet; COMPLETE required |
| TASK-SITE-INTEGRATE-006 | BC011–018 | Root: contracts.mjs/bootstrap.mjs/build-site.mjs/build entry/vendor/new project policy+csproj/solution/governance/workflows/docs/removal | Serialized shared edits, join all packets, inspect every diff, fix boundaries, static build and manual browser; do not overwrite unrelated work |
| TASK-SITE-REVIEW-007 | BC011–018 | gpt-6-astra ultra, read-only | All required packets COMPLETE + integrated evidence; complete only with no unresolved blocking finding |
| TASK-SITE-COVERAGE-008 | BC024 | gpt-6-luna high: new SiteCoverage-prefixed C# helpers/tests only | Strongest COMPLETE coverage decision and accepted exact source/threshold/report contract; no changes to original test/probe files. Real native ranges/converter regressions/after-session gate; no local tests; COMPLETE packet required |
| TASK-SITE-BROWSER-009 | BC025/011–018 | gpt-6-luna high: new SiteBrowser-prefixed C# helpers/tests plus SiteStaticFileHost.cs only | Complete TEST004 and frozen BCL real Chrome/HTTP/CDP contract; no source/config/probe/coverage-file edits. Existing browser availability/version, real generated UI and precise native ranges; no local tests/installations; COMPLETE packet required |
| TASK-SITE-ANALYZER-COVERAGE-011 | BC027 / CQ006/009 | gpt-6-luna high: new scripts/Features/CodeQuality/site-analyzer-coverage*.ps1 and new SiteAnalyzerCoverage-prefixed AnalyzerTests only | Strongest TASK010 COMPLETE native collector/count/pipeline contract plus root frozen JSON/spec/ADR/policy precede writes. Tests first; disjoint from JS/browser/current analyzer source. No local tests/installations or shared config edits. COMPLETE packet and root full source/build/GitHub join required |
| TASK-SITE-BROWSER-REPAIR-012 | BC012/013/014/015/024/025 | gpt-6-luna high: SiteBrowser-prefixed C# and SiteStaticFileHost only | Strongest REVIEW007 complete read-only blocker packet plus original009 source packet; fix real CDP wire/MIME/waits/media/oracle/abort/geometry/lifecycle/coverage omissions, preserve every original assertion. No production JS/coverage/helper/config edits. Complete source/build evidence, root full review and real GitHub join required |
| TASK-SITE-COVERAGE-REPAIR-013 | BC024 | gpt-6-luna high: SiteCoverage-prefixed C# only | Strongest COMPLETE converter review before writes: preserve receipt/script snapshots, resolve ranges per snapshot then union, infer only proved physical-function outcomes, real exact fixture regressions and bounded process cleanup. No SiteBrowser/process helper/config/source edits. COMPLETE packet, root full review/build and GitHub numeric evidence required |

The initial runtime declined a third coding worker with `agent thread limit reached`; the two available Luna workers implemented visual and scene scopes first. Reuse the first completed worker for tests, then reuse the next completed worker for evidence after the frozen test protocol is read. The strongest planner remains available for final review. Root integrates shared state while disjoint work proceeds. No native worker commit/push, global installation, local TUnit/tests/load/container qualification, central configuration/schema/public API change or out-of-scope file write. Stop and escalate missing APIs/dependencies, policy conflicts, changed contract, overlap or unproved semantics. States complete/blocked/failed/cancelled; only reviewed complete packets unblock join.

## Ordered checklist and verification

- [x] Read policy, current site/docs/workflows; baseline browser appearance and numerical contract; strongest plan and bounded Claude consultation.
- [x] Brainstorm, stable acceptance and this plan before implementation; Feature/ADR contract required before workers.
- [x] Record BC011–018/ADR040, root DOM/probe contracts and project policy before writes. Frozen evidence/probe/build protocol: `site/Features/BenchmarkComparisons/protocol.md`.
- [x] Relevant baseline: read exact successful historical GitHub run36926803549 at source9c570f8c33a7a9667507a8e1c0ca68860de3be45, all four successful jobs and unchanged comparison artifact; keep unrelated current Core errors tracked in owning [quality evidence](docs/implementation/code-quality.md). New site TUnit baseline has no existing run; first CI is required, not a claimed pass.
- [x] Vendor official Three0.186.1 after SHA512 check; record original bytes, SHA256, MIT and size/exception; start disjoint visual/scene workers. Test worker follows a completed scope because of the runtime agent limit.
- [x] Join original TEST004 protocol and EVIDENCE005 complete source packets; root implement independent bootstrap/build and narrow shared integration. Added native coverage/browser/analyzer packets have separate mandatory joins below.
- [x] Join every complete result and inspect all diffs/source hashes; migrate flat assets/test runner only after proof.
- [x] Original production source syntax/limits/links/vendor/raw-byte/workspace-governance checks and isolated authentic-evidence preview build. Newly repaired test/tooling packets still require integrated checks below.
- [x] Manual real-browser desktop1440/1280, tablet768, mobile390/320, keyboard/controls/provenance/negative state and Three lifecycle packet at recorded production source hashes; fix findings without local qualification claims.
- [x] GitHub full relevant site TUnit suite at exact candidate source; inspect every result/artifact and retain raw measured provenance. No public deploy/DNS in this task.
- [x] Join strongest final review; all artifacts/AC states/delivery limits recorded; website coverage/publication are fulfilled by the closure below, with broader product quality/coverage and advanced profiles separate.

No per-test existing failure baseline is available for the new SiteTests project. Current broader source reports1180 initial solution errors and652 later Abstractions errors; their root-cause/fix ownership remains [memory repair evidence](docs/implementation/memory-performance.md), not site changes. If the new suite fails, add each actual failure here with root cause and verify its repair in GitHub.

Final order: source/contract review → development build/static source and governance → real isolated preview/browser design packet → GitHub TUnit suite → joined strongest final review → honest evidence record. Existing WebGPU/Three.js guidance is applied, no skills/tools installed. Complexity analyzers are centrally configured; native coverage gate implementation and numeric GitHub qualification remain pending. No invented threshold pass. Pages publication continues to require its successful-source/evidence boundary and is deferred.

## Coverage gap closure before completion

The strongest reviewer identified that an honest pending coverage statement cannot satisfy the root thresholds. TASK008/009 extend the accepted verification scope before writes; they run in parallel with disjoint ownership after original TEST004/EVIDENCE005 COMPLETE packets. Root integrates shared workflow/settings and waits for both complete results. No new tool/browser/package installation or local test execution. Missing real browser, failed numeric thresholds, source mismatch or unresolved converter semantics stops completion and escalates to root; workers must not weaken thresholds or add exclusions. The exact implementation contract and required inventory are in ADR-040.

- [x] Join coverage converter source/regressions and real-browser suite, inspect every diff and source hash.
- [x] Development build/format/static review with all central analyzers; repair without suppressions.
- [x] GitHub collects native Node/browser coverage and existing MTP analyzer coverage, retains exact source/runtimes/ranges/report/baseline, and enforces all site thresholds.
- [x] Join strongest final review after every COMPLETE packet and integrated browser/CI/coverage evidence.

Analyzer dependency order: root freezes contract and central18.11.2 existing collector pin → TASK011 authors parser boundary/failure TUnit tests before gate helpers → pre-test source manifest/settings → complete real AnalyzerTests collection → unchanged-source/raw-integer coverage gate → retain XML/report/TRX/SARIF/runtime/source → joined review. Required source/critical-pipeline inventory is the JSON contract in scripts/Features/CodeQuality; ADR-033 defines semantics. Gate requires missing report/module/file, zero denominator, malformed counts/DTD/paths, conflicting duplicates and exact80/70/90 regressions. No skipped test, ignored exit, source exclusion or fake dependency. Dependencies blocked/failed do not unblock candidate delivery. Root owns workflow/settings/pins/docs and candidate snapshot; no worker commit/push. Rollback removes the coherent candidate gate addition without weakening policy or deleting original evidence. Broader AC-CQ-009 stays pending.

### Active repair scheduling and traceability

Integrated development build after TASK011's complete source packet found three
compiler quality failures; this is not a local test result. The same bounded
worker owns the repair within its existing SiteAnalyzerCoverage-prefixed files,
preserving all assertions, then returns a refreshed hash packet before rebuild:

- [x] CA1822 SiteAnalyzerCoverageTestScope.ReadJson: make the utility static and
  update every own caller, or use real instance data without a dummy access.
- [x] CA1834 SiteAnalyzerCoverageFixture.AppendLineCoverage: append the named slash
  as a char while preserving exact generated native XML.
- [x] IDE0005 SiteAnalyzerCoverageProcessTests: remove its unused System.Text.Json
  using; no analyzer suppression or severity change.
- [x] TASK007 native-format finding: Microsoft's real Cobertura emits branch=True
  and branch=False. Normalize valid native boolean spellings before duplicate
  comparisons; retain invalid-token rejection and add actual-spelling regressions.
- [x] TASK007 integer finding: ordinary PowerShell Int64 overflow promotes to
  double. Use BigInteger cross-products and checked Int64 aggregate additions;
  test one-below/exact huge70% boundary and aggregate overflow without fabricated
  measured evidence. Preserve exact80/70/90 and all previous tests.
- [x] TASK007 source-bound finding: enforce positive native line numbers within
  the exact hash-verified source's physical line count; test zero/out-of-range and
  last-line boundaries. Replace100-lines-per-source XML fixtures with at most10
  real source lines and an explicit aggregate line budget, preserving independent
  count/exit/pipeline assertions. Do not pad or change production sources.

TASK012 returned its complete browser repair packet. Root reused that worker for
TASK013 after starting/restarting a separate worker returned `agent thread limit
reached`; TASK013 and TASK011 now own disjoint coverage and analyzer-tooling scopes.
No implementation is skipped and no partial packet satisfies join.
Root handles only shared configuration, documentation and candidate preparation
while both independent current workers run. The analyzer dependency requirement is
BC-027; BC-026 remains owned by the concurrent TimeSeries feature. Every own
acceptance/ADR/tooling-contract reference was updated before candidate delivery.

After TASK013 completes, the same bounded worker closes TASK012's navigation
follow-up in SiteBrowserNavigation.cs and SiteBrowserTokens.cs only. A real manual
Chrome Page.getFrameTree receipt separates frame.url from optional urlFragment;
compare their concatenation with the requested URL, preserve frame/loader identity
and document.readyState/location.href checks, and retain the existing real fresh
#benchmarks navigation test. Do not weaken navigation or error assertions. Root
reviews both hashes and the actual GitHub browser result before join.

### Integrated SiteTests development build repair

The complete TASK012/013 source packets were joined for a development build;
`dotnet build tests/KeyLoad.SiteTests/KeyLoad.SiteTests.csproj --no-restore --configuration Release`
returned14 mandatory compiler/quality diagnostics. The same bounded worker owns
only SiteCoverage*.cs, SiteBrowser*.cs and SiteStaticFileHost.cs for this repair;
all production/probe/process-shared/config/docs files remain root/other-owned.
Preserve actual resource ownership and all original assertions, without suppression.

- [x] CS0103 NativeTestSupport.FindFixtureScript: reuse the exact source URL/scriptId helper at its correct ownership boundary.
- [x] CS0103 NativeTestSupport.JsonNode: add its actual required namespace or move assertion coherently.
- [x] KLD0033 RuntimeErrors: reduce nesting4 to mandatory3 with cohesive helper extraction.
- [x] CA1308 StaticFileHost.ContentType: use case-insensitive suffix lookup or matching uppercase named suffixes; preserve real MIME assertions.
- [x] CA2000 BrowserProcess.StartAsync: make process-wrapper ownership/disposal explicit on every failure path.
- [x] CA2234 StaticHostTests.AssertContentType: use actual Uri overload.
- [x] CA2234 StaticHostTests aborted response request: use actual Uri overload.
- [x] CA2234 StaticHostTests follow-up fetch: use actual Uri overload.
- [x] CA1859 BrowserChrome.StopCoverageAsync: preserve exact Task<JsonElement> result type.
- [x] CA2000 BrowserSession browser startup: close ownership transfer/failure disposal.
- [x] CA2000 BrowserSession temporary directory: close ownership transfer/failure disposal.
- [x] IDE0200 SnapshotResolver: use the existing physical-function predicate method group.
- [x] IDE0005 BrowserChrome: remove unused namespace.
- [x] IDE0005 BrowserSession: remove unused namespace.
- [x] TASK007 real-node oracle: V8 can omit nestedNeverCalled within an uncalled parent. Keep line8 uncovered; assert nested physical identity using the actual lazy closure within executed choose, with native output only.

Root waits for the refreshed COMPLETE packet, reads every changed diff, rebuilds
with all existing analyzers and then joins the exact-SHA GitHub suite. Existing
native coverage semantics/thresholds remain mandatory. A development build cannot
qualify runtime behavior or mark any failing test as passed.

### Native same-span function repair

TASK007 verified the V8 native contract before the next TASK013 semantic write:
script wrapper and an uncalled function can have identical spans, with the native
innermost function last. Preserve native function order; resolve equal-span ties
to that last function within one script snapshot for line state and omitted-child
inference, rather than OR-ing an executed wrapper into an uncalled function. Apply
the same selected physical function before explicit branch-outcome checks; function
names remain metadata and never create denominators. Add a real Node-produced
single-function file without a trailing newline, assert equal-span native ordering
and that its uncalled function stays uncovered. Preserve the original9/8line2/2
branch and reversed-receipt checks. The same TASK013 worker owns this bounded
SiteCoverage-only correction and refreshed packet; root rebuild/review/GitHub join
are mandatory. No invented duplicate tuple or native receipt is acceptable.

Native converter regression evidence must survive temporary-directory teardown.
TASK013 retains unchanged real fixture source, original Node receipts, stdout and
raw SHA256/identity metadata under a separate converter-fixtures subtree of
KEYLOAD_SITE_COVERAGE. These files are uploaded for review and never enter the
production node/ or browser/sessions/ coverage denominators. Preserve original
native bytes and cleanup of owned temporary files; mutated malformed/crossing
copies must not be labeled or retained as actual executed native evidence. Root
reviews the retained packet and actual GitHub converter assertions before join.

### Reviewed repair evidence and remaining joins

The integrated SiteTests Release development build now passes with0warnings and
0errors. This closes its14compiler diagnostics only; no local tests were run.
TASK013 received strongest source approval after correcting an explicit-zero
branch outcome to continue across later snapshots. All18 coverage-source files
have an ordered path/hash manifest SHA256
`96fb83c07c8855a3324fe1402adfea2a7b008b954c045393fe381bccac8df3df`;
native9/8line2/2branch/reversal and equal-span regressions still require GitHub.

The unexpected Browser overlap was traced to the other chat's completed
KL-SITE-BUILD-003A worker, turn01a0fbc0-9bee-7fc3-8225-adc3e7aa47a5.
Root read its changes, preserved its startup/process ownership repairs and
notified that chat to stop further SiteTests writers. TASK012 is again the sole
Browser/StaticHost writer; it removes duplicate CDP disposal while retaining
guaranteed HTTP/process teardown, then returns a complete refreshed source packet.

- [x] TASK011 module boundary oracle: independently assert exact counts and the
  complete12critical-pipeline status/failure matrix. Module79/80/81percent can
  still fail90percent pipelines; overall exit/pass must reflect all failures,
  rather than only the module label. Preserve the exact module denominator250.
- [x] TASK011 physical boundary oracle: use one named hash-verified source's real
  final physical line and final+1, retaining at most10 emitted records per source.
  An11line fixture within a16line source is not an out-of-bounds regression.
- [x] Join final Browser/StaticHost packet and strongest source approval.
- [x] Rebuild AnalyzerTests and verify focused format after every source join.
- [x] TASK011 numeric source audit: flatten LineBudget's foreach/if/else-if/ternary
  depth4 to mandatory3 without changing any line budgets. The real compiler
  NumericAnalyzerSelfInventoryTests is the existing acceptance regression;
  development build/format alone does not run that fixture or qualify this rule.
- [x] Prepare the reviewed isolated source candidate, verify its actual inventory,
  preserve real HEAD/index, dispatch exact-SHA GitHub qualification and inspect
  all required tests/native numeric artifacts before final acceptance.

All bounded source repairs are joined after full root diff review and strongest
final source approval. The51-source ordered manifest SHA256 is
`98c457ee0333c816795665f742101989b9a0638b2382372313afefd823de4617`;
root canonical packet is `/private/tmp/keyload-site-final-source-joins-20261002.json`.
Both independent Release development builds pass0warnings/0errors; focused
formatter verification passes for analyzer production, AnalyzerTests and SiteTests,
and workspace governance passes25projects/4modules. These source/checklist repairs
do not qualify runtime tests, native coverage percentages, the full solution or
final TASK007. Those remain mandatory exact-candidate GitHub joins.

### Exact-candidate GitHub failure loop

Candidate `f56d511b4a343eff99d3117e9414490a05cfb8ae` was composed through a
temporary index and pushed to `codex/site-qualification-20261002-a`; shared
HEAD/index and runtime changes were preserved. Source snapshot470files,
323inputs,23projects/28policies passed actual candidate governance and strongest
full tree audit. Source-only receipt SHA256 is
`271d8415d29c69e8af84f604858079ed6effd4ce586d06220237ff9ac57b9684`.

- [x] GitHub run36987695573, validate job110776384044: runtime receipt step
  failed before tests because hosted runner has no `rg`. Replace only the report
  file inventory command with native `find -type f -print0`, C-locale null-delimited
  sort and SHA256; retain all source/runtime/real-report hash receipts. No tool
  installation, missing-test pass or threshold change. Root exclusively owns
  workflow integration; one-line portable enumeration repair is not delegated.
- [x] Create an ordinary descendant candidate commit of the exact failed SHA,
  using another fresh temporary index, retaining real HEAD/index and all other
  candidate blobs. Push without force and dispatch validation again.

The first run is a failure, not a test pass: AnalyzerTests and SiteTests were
skipped; native coverage verification correctly failed for a missing report;
publish was skipped. Retain its run/job URLs, logs and uploaded artifact when
reviewing the descendant. Every real runtime failure continues this checklist.

Reviewed descendant `b402dc50785028cc5b09a3928c5b28ba8a721a6e` changes exactly
the workflow enumeration and these plan/status records; all470 parent blobs were
verified before composition. The current shared HEAD/index were preserved, and
the same candidate branch was advanced without force. GitHub run36988549282,
validate job110779101338, is executing at that exact SHA. Its runtime/source
receipt step passed, closing the unavailable-`rg` repair only. Analyzer tests,
native coverage and SiteTests are still pending and cannot count as passed.

The completed run36988549282 executed88 AnalyzerTests:84 passed,4 failed,0 skipped.
Its original XML SHA256 is
`983f7de7132938062db791723cdfba1ca683eceb98b3cecb191b2452af938091`.
Artifact11217729758 and its immutable native XML/TRX/source/PDB/log receipt are
retained under `/private/tmp/keyload-site-run-36988549282`. SiteTests were skipped.
The native gate failed before producing counts because genuine Microsoft decimal
display percentages did not match the integer-only display grammar.

- [x] ControlFlowDepthTwoAndThreePassWhileFourReportsAsync: its expected method
  span includes four indentation characters; actual line2/column4 starts at the
  first `internal` token. Correct the independent exact-span oracle, preserving
  all depth2/3/4 cases, severity, path, line, column and span-end assertions.
- [x] ExecutableUnitLinesAtLimitPassAndOneOverReportsAsync: same leading-trivia
  oracle defect; preserve49/50/51 boundaries and exact full executable span.
- [x] DisabledTextInsideExecutableUnitContributesToItsTotalAsync: same leading
  trivia defect; retain real disabled text, code-line totals and exact location.
- [x] AnalyzerAndAnalyzerTestsSourcesSatisfyNumericPolicyAsync: cohesive test
  classes currently246/210 code lines exceed200. Split fixture responsibilities
  without deleting cases/assertions, hiding files or using partial-type loopholes.
- [x] Native Cobertura decimal display: support genuine16.67%,62.5%,83.33%,91.67%
  spellings while exact integer pairs remain the sole threshold inputs; add real
  PowerShell-process regressions and strict malformed-token rejection.
- [x] Required critical-pipeline coverage gaps: independently inspected original
  XML has KLD0001=163/224 and KLD0022=25/30 covered lines. Add meaningful real
  analyzer diagnostic flows for those gaps, preserving every source denominator
  and90% threshold. These read-only counts are not an accepted gate baseline.

Root will freeze the bounded failure contracts and disjoint task014/015/016
ownership in acceptance, feature and ADR before any new write-capable worker.
Full native analyzer gate plus complete site suite must then pass at an ordinary
descendant candidate SHA; no partial or skipped suite can close this checklist.

#### Bounded failure task graph

| Task | AC IDs / owner and tier | Exact scope / dependency / join |
|---|---|---|
| TASK-SITE-NATIVE-FORMAT-014 | BC-027/CQ-009; mcaf_local_governance, economical capable gpt-6-luna high | Only shared.ps1 BranchCoveragePattern and new SiteAnalyzerCoverageBranchDisplayTests.cs; start after strongest frozen acceptance/ADR approval; tests-first; COMPLETE exact source hashes and all native display/count/error assertions before root joins. |
| TASK-SITE-NUMERIC-REPAIR-015 | BC-027/CQ-008; site_browser_repair, economical capable gpt-6-luna high | Only the two original numeric test classes and two new extended-construct/span-trivia test classes named in ADR-033; independent of014; preserve all17original registered cases, changing only the three indentation-start oracles; COMPLETE method/assertion movement map and all4source hashes before root joins. |
| TASK-SITE-DIAGNOSTIC-FLOWS-016 | BC-027/CQ-009; mcaf_local_governance, economical capable gpt-6-luna high after014 | Only new LiteralMachineKeyInvocationTests.cs, LiteralMachineKeyConstructionTests.cs, LiteralMachineKeyAttributeContractTests.cs and SystemClockSemanticTests.cs; exact BCL/SDK semantic cases frozen in site acceptance/ADR; start after strongest case approval and014 COMPLETE join; no source/config/014/015 overlap; COMPLETE real-input/assertion/case/hash packet before integration. |
| TASK-SITE-RETRY-JOIN-017 | BC-017/024/025/027; strongest root integration/final review | Depends on every014/015/016 COMPLETE source packet; inspect every diff, validate unchanged analyzer denominators/native integer semantics and registered case inventory, enabled builds/format, ordinary descendant compose/push, full exact-SHA GitHub analyzer/native/site/browser/numeric evidence; skipped/failed/blocked work never unblocks final acceptance. |

Root owns every shared contract/config/workflow/feature/ADR/status/candidate edit.
Worker verification: no local tests; source-only diff/registration/limits checks,
`dotnet build tests/KeyLoad.Analyzers.Tests/KeyLoad.Analyzers.Tests.csproj --no-restore --configuration Release`
and scoped `dotnet format ... --verify-no-changes --no-restore` only when root
serializes builds. Root executes static governance and the complete exact-SHA
GitHub workflow, with original failed XML/TRX retained. All workers stop/escalate
on changed contracts, actual production defect, ambiguous native semantics,
ownership overlap, failed static/build gate or need for dependencies/exclusions.
Completion states are complete/blocked/failed/cancelled with artifacts; only
reviewed complete packets satisfy joins. GitHub runtime/native qualification
remains pending after source completion.

- [x] TASK015 strongest latent self-inventory finding: the original
  ElseIfEveryLoopUsingStatementAndFixedAddNestingAsync still exceeds50unit LOC
  because NumericCodeLineCounter counts its whole raw-string token, including
  interior blank lines. The same owned extended-construct class extracts the
  exact compiler input to a named private const; root compares decoded bytes,
  all17 registrations/assertions and new hash, then build/format/actual CI.

TASK014 and015 source joins are now COMPLETE with strongest approval. The
extracted fixture's correct decoded receipt is699UTF8bytes and SHA256
`d402a5f38f1eb28daa90714c92ec45906c833ea7eef936893fd44875c1eb3f01`; method21
token-bearing lines including[Test], updated file SHA256
`cbf7de87e6c2209979d148737e0bb937f061567198c7b0c011750dfaa0df9188`.
Their runtime repairs still require GitHub. TASK016 is BLOCKED on a retained real
WriteString key/value regression; its partial source is not a completed dependency.

| Stage | Owner / scope | Dependencies and completion |
|---|---|---|
| TASK-SITE-ARGUMENT-ROLE-018-RED | mcaf_local_governance, gpt-6-luna high; only two new binding test files named in acceptance/ADR | Start after strongest frozen contract approval; compiler-valid semantic cases/independent spans; complete source packet plus014/015 joins allows a separate expected-red GitHub source cut, never017 acceptance. |
| TASK-SITE-ARGUMENT-ROLE-018-FIX | Same economical worker; only MachineKeyLiteralClassifier.cs and MachineKeySemanticSymbols.cs | Start after exact-SHA GitHub regression failures and strongest reviewed two-file contract; one shared bound-parameter role decision, unchanged source/catalog/policy boundaries; COMPLETE every diff/hash/semantic case and numeric source review. |
| TASK-SITE-DIAGNOSTIC-FLOWS-016 resumed | Same existing four-new-file semantic map | Wait for018-FIX reviewed complete source; preserve its retained failing case and all accepted positive/negative/edge flows; COMPLETE four-source packet before017. |
| TASK-SITE-RETRY-JOIN-017 | Strongest root | Still waits014/015/018-FIX/016 COMPLETE joins and all exact-SHA native/site gates; a red baseline, partial source or blocked worker never unblocks final acceptance. |

Ordered verification:018 tests-first source → enabled AnalyzerTests development
build and focused formatter → approved immutable descendant/source receipts →
full GitHub red regression baseline with unchanged production → bounded018 repair
→ all source joins/016 resumed → enabled builds/format/governance → exact-SHA
complete AnalyzerTests/native/site suite → strongest final review. Every actual
failed case/count/artifact remains in this plan/status; no local tests. Red source
cut is required to prove the defect, not an attempt to declare partial work done.

018-RED's11 new binding cases and retained016 regression are source-complete;
enabled AnalyzerTests development build passes0warnings/0errors. No semantic
fixture or TUnit case has run locally. Root prepares an immutable red source cut
from parentb402 with five owning docs, shared parser regex, four numeric files,
new branch-display class, retained invocation regression and two new binding
classes. The informational BenchmarkComparisons pointer stays in the workspace
with another owner's changed TimeSeries row; it is excluded from this bounded
CodeQuality red snapshot to preserve concurrent implementation/evidence ownership.
Existing BC-027 feature links and all required acceptance/ADR/CQ contracts remain
in the candidate. Root must inspect the complete temporary-tree/diff/hash receipt,
preserve shared HEAD/index, advance the branch without force and run real GitHub.

Approved red candidate8d8d395f7fa6157773e0318d64f0124cd2e84579 has476files and
exactly8modified/6added paths fromb402. All30 production analyzer sources are
unchanged. Composer/input/full-diff review passed before writing; real shared
HEAD3559225a5f918160e46e32c9a812c3f71790e382 (advanced by runtime owner) and
index67897132a94dac17bdd09d8936ae908c8de585a155a0a50d7e0962cec22e914b were
preserved. No force push or main mutation. GitHub run36991420593, validate
job110788278234, executes the full expected-red analyzer/native baseline.
018-FIX awaits its actual failures and strongest release;016 stays blocked and
017/final007 remain unqualified. No red result or source receipt counts as pass.


Completed tests-first run36991420593 at exact8d8d395f7fa6157773e0318d64f0124cd2e84579
executed103cases:96passed,7failed,0skipped. All original numeric/span/self-inventory
failures and all3 genuine decimal-display process cases now pass. All11 new
binding fixtures compiled successfully, including the real reduced/static
ASP.NET extension and expanded-params case. Retained raw artifact11219323506
digest590518a45e2ee52b183949899c18bc5aa0e56c7431e2aec20743cd20ec2a51c0,
TRX, complete logs, source/PDB/SARIF and native XML under
`/private/tmp/keyload-site-run-36991420593`.

Actual native XML SHA256
`aea1ab67c5c15dbd76dc2bed6c0ba8eb5bb1a31ca14306195171cd43ca1abe60`;
the corrected strict parser reports module889/942lines and472/570branches.
KLD0001passes205/224; KLD0022still fails25/30. These historical counts cannot
qualify changed production source. SiteTests were skipped and final017/007
remain pending. The bounded production repair must resolve these actual failures:

- [x] Utf8JsonWriterValueArgumentIsNotAMachineKeyAsync: expected1actual2; nested
  AlwaysKey helper wrongly promotes a bound human value.
- [x] DynamicPropertyLookupUsesNamedAndPositionalKeyFallbackAsync: expected2actual3;
  unresolved name fallback wrongly promotes a named non-key value.
- [x] NestedKeyConcatIsPositiveAndNestedValueConcatIsNegativeAsync: expected2actual3;
  nested AlwaysKey lookup needs the nearest argument's shared bound role.
- [x] ReorderedNamedConstructorsBindKeysNotFirstSourceValuesAsync: expected2actual4;
  source argument0 is a bound value, not constructor logical parameter0.
- [x] DictionaryAddBindsPositionalAndReorderedNamedKeysAsync: expected2actual3;
  source argument0 is a bound value, not keyed method logical parameter0.
- [x] Utf8JsonWriterPositionalArgumentsBindPropertyAndValueAsync: expected1actual2;
  ordinary/nested classification must share bound key-versus-value decision.
- [x] Utf8JsonWriterReorderedNamedArgumentsBindPropertyAndValueAsync: expected1actual2;
  named bound value remains negative even when written first.

Root independently inspected all7 TRX failures and integer-native report.
Strongest reviewer now assesses this real red receipt before explicit018-FIX
release. TASK016 stays blocked until the two production files' complete reviewed
join; no altered oracle or skipped test can satisfy acceptance.

Strongest evidence review is now COMPLETE and explicitly releases018-FIX.
The economical existing worker owns only the two frozen production files.
Root owns docs and integration;016 stays blocked until the complete source join.


Canonical migration cleanup is now applied in the shared workspace too. All5
superseded flat assets/Node test were independently byte-equal to their historical
9c originals and already absent from the reviewed candidate tree. Replacement
source packets/manual rendering are joined; deleting those unchanged old sources
preserves the canonical TUnit ownership. No unrelated edits or real Git index
were changed. Runtime qualification remains required.

Read-only hosted-runtime preflight is COMPLETE: no concrete validation setup
blocker. Its separate legacy publication path lacks mandatory browser/coverage
inputs and may select measured source predating this suite. ADR-040 records this
deferred delivery boundary; do not publish or claim deployment readiness in this
design validation task. A future two-revision release must qualify its real path.


018 source integration rejected the first worker packet before qualification:
explicit-key positives were incorrectly gated behind method/type heuristics,
unresolved fallback selected a later first-unnamed argument, and operation-owned
extension ordinals needed parameter-owner normalization plus bound params fallback.
The same two-file worker corrected all findings under the frozen contract.

- [x] IDE0019 constructor-binding build failure: replace the as/null sequence with
  actual `is not IMethodSymbol constructor` binding. No suppression or behavior change.

Revised classifier SHA256
`6daafb389c2caca0976ea7c8530cae37a6ba033f5fb3001171a2489bfeef1218`;
semantic helper SHA256
`bf33778bb16a12d04b07a88d535a516d004957592a2aba9d5b91845e2e0de5fc`.
The enabled AnalyzerTests Release development build now passes0warnings0errors;
no TUnit qualification has run locally. Strongest final source review precedes
016 resumption and complete GitHub verification.


018-FIX strongest source review is COMPLETE and approved at the two hashes above;
only2 of the30 inventoried production files differ from RED. Root independently
read every diff, enabled Release build passes0warnings0errors, analyzer formatter
passes. No tests ran locally. TASK016 is now released in its exact four-file
semantic scope; shared fixture/config/catalog and all source paths stay frozen.
017 and final007 still wait for its complete join and actual GitHub qualification.


TASK016 is COMPLETE with strongest/root full source review. Its original retained
WriteString method is byte-preserved against RED; all10 registered flows use
real SDK/BCL compiler inputs and meaningful complete result-set assertions.
Invocation59d37b047201927d46d23484fc62076c09101f97f55bcc35d01de78fe7f30c7a,
Construction2d6e3a24ca68a93bc666d6a7176532ddd66c52ed4e88c1f71fa0576251f7fd28,
Attributes45bb3146d364a54dd2d7f52686e9fe1208aee5688c9183eecf79cda3e19676cd,
Clock61ce61fabea5c4a8b850bf68560c26bf39fde454328eada16bb3fcd22f883e06.
The four-file ordered manifest is213298217ea9f5309765bb41eca581062cc1ca78ef45abd4cd4715dd26cd41cb.
Root AnalyzerTests and SiteTests Release development builds both pass0warnings
0errors; scoped analyzer/test/site format and static workspace governance pass.
No local tests ran.017 may now prepare its ordinary descendant source cut from
RED; final007 remains gated on actual native and complete GitHub site results.


TASK017 strongest transport review COMPLETE before composition: exact11paths
8modified3added, every476parentblob verified,479finalfiles. ADR040's whitespace
handling is explicitly workspace-only to preserve the bounded isolated snapshot.
Reviewed composer c8fa1941f0d6d9af0784ed70f3ccbf39aedbac25c398cc21fb314aa329529823,
inputs4204929fc5c398e117854548eabcea551cd0feba6959519297b94c2a9e238d7b,
proper immutable-parent diff4e19d8a0e575b3c3ca970739336f3b93303926031478ca6ffdbc936f97939e43.
Ordinary candidate7c50573adb92eec67a7a3209f11be97a7688bd3c preserves shared
HEAD3559225/index67897132a94dac17bdd09d8936ae908c8de585a155a0a50d7e0962cec22e914b.
Normal branch push succeeded; real full GitHub run36994330874, validatejob110797469875
at that exactSHA is executing. Publish skipped. No passing qualification yet.


Run36994330874 is completed FAILURE at7c50573. Analyzer112/112/0/0 passes
and native exact-source935/981lines528/612branches passes module80/70 and
every12pipeline90. Native XML SHA256b7514a3a32072bf0f567200f2abfdfff3942b116709e9f4aea088511b14367ab.
The first SiteTests execution has28results:21passed7failed0skipped. Retained
artifact11220962516 digest2ad13a414cafd5a26d327bc0f5144a9c2e5bc7dfa8cf149d84fdb23074f26cd7
contains every TRX, raw native Node range, failed coverage report and source hash;
complete workflow log is in/private/tmp/keyload-site-run-36994330874/workflow.log.

- [x] AC_BC_016_CommittedSourceOutputPreservesBothSiteAndMeasuredRevisions: builderexit1.
- [x] AC_BC_016_RealBuilderEmitsCompletePreviewWithAuthenticRawBytesAndNoScriptEvidence: builderexit1.
- [x] AC_BC_015_ProductionValidatorsAcceptAuthenticReportsAndBuilderCatalog: builderexit1.
- [x] AC_BC_015_CatalogValidatorRejectsPathHashRevisionAndCompletenessViolations: builderexit1.
- [x] AC_BC_015_LoadReportVerifiesAuthenticBytesAcrossRealHttpAndRejectsTampering: builderexit1.
- [x] AC_BC_025_RealChromeControlsMatchAuthenticReportsAndRetainNativeCoverage: samebuilderexit1
  before Chrome launch; current exception incorrectly labels it browser startup.
- [x] AfterTestSession coverage failure: no browser execution, module476/1806lines
  and59/81blockoutcomes; four of fivecritical modules fail; measurements passes.
  Missingbrowser failsclosed.

Root/strongest raw source hash and native branch evidence isolates one shared
root cause: verifyVendor's gzip-count equality against a recorded Node26 count.
Raw official vendor SHA/bytes/license/version/source/integrity remain correct;
hostedNode22.23.3 completes gzip then throws on size mismatch before verifyAssets
or report validation. Hosted actual compressed length was not retained; invent
none. Negative symlink/corrupt-input exit1 cases currently mask this unrelated
vendor failure; require scenario-specific failure evidence before accepting them.
Freeze bounded portable identity/runtime-measurement and diagnostic contracts
in acceptance/ADR before new workers. All passed analyzer evidence stays retained;
final site/007 acceptance remains blocked on complete real GitHub suite.

## Actual SiteTests failure repair graph

Brainstorm and acceptance were extended before this plan. Existing observed
run36994330874 is the full relevant baseline; every seven failure checklist items
above remain open until the complete new GitHub gate passes. No local tests.

| Task | REQ/AC | Owner/model and exact write scope | Dependencies, start, artifacts, verification and join |
|---|---|---|---|
| TASK-SITE-VENDOR-PORTABILITY-019 | BC016/017/024 | mcaf_local_governance, gpt-6-luna high: site/Features/BenchmarkComparisons/build-site.mjs; NEW SiteVendorBuildTests.cs, SiteVendorTestScope.cs, SiteVendorTokens.cs only | Root frozen acceptance/ADR + strongest COMPLETE approval before writes. Tests first against real isolated source/vendor/authentic reports; immutable official repository vendor. Runtime compression receipt exactly frozen. Return complete/blocked/failed/cancelled, every full diff/hash/test mapping/limits/static packet. Root serial development build/format and strongest review; complete GitHub gate required. |
| TASK-SITE-BUILDER-DIAGNOSTICS-020 | BC016/017/025 | site_browser_repair, gpt-6-luna high: SiteBuildSupport.cs, SiteBrowserSession.cs, SiteBuildTests.cs, SiteEvidenceValidatorTests.cs; NEW SiteBuilderDiagnostics.cs, SiteBuilderDiagnosticsTests.cs, SiteBuilderTokens.cs only | Same contract approval; independent disjoint code from019, execution depends on019's portable production builder. Preserve signatures/process ownership/all registrations. Exact bounded persistent receipt and specific negative errors. Return terminal full diff/hash/assertion/limits packet. Root joins019+020, strongest full review, development build/format/static governance, then complete real GitHub suite. |

- [x] Strongest approves the recorded019/020 acceptance/ADR/task graph before writes.
- [x] Join019 portable strict identity/runtime receipt and real regression packet.
- [x] Join020 retained actual diagnostics, accurate failure label and precise negative cases.
- [x] Root and strongest inspect every production/test diff and all unchanged vendor identities.
- [x] Serial enabled Release development builds, scoped format and static governance pass.
- [x] Ordinary descendant candidate preserves shared HEAD/index/unrelated work; approved transport.
- [x] Complete GitHub Analyzer/native112+ full SiteTests/Chrome/JS80/70/90 receipts pass.
- [x] Close each actual failed-test item with exact new source/run/job/artifact evidence.

Escalate unknown schema, inability to retain bounded actual data, fake inputs,
source ownership overlap, or quality limits. Do not edit shared tokens/config,
packages, collector/converter, vendor or workflow from these worker scopes. Root
alone owns integration docs/workflow and any separately approved scope extension.

## Owner-requested fresh-evidence publication extension

The owner now requests a separate deployment action collecting fresh JSON from
the actual comparison job after tests. This supersedes the earlier task-local
publication deferral only for the new approved workflow scope; DNS remains
deferred. Strongest read-only architecture planning is in progress. Freeze stable
REQ/AC-BC-028, two-revision source/job/artifact/freshness contracts, automated and
actual GitHub test strategy, ownership and ordered joins before delegated writes.
Keep ci.yml/runtime/TimeSeries/README/global status under their existing owners.
No new publication result is claimed; current legacy publish configuration is
known unqualified and must be replaced coherently, never bypassed or counted green.

TASK019/020 write release follows strongest COMPLETE recorded-contract approval:
brainstorm a6d9b7c7aa0edc764350d0e1a8b1c7b610bb537e09b4df39420c937238594377,
acceptance85a3d7b869296f2fd4aafdcb81caef91b26b5ad871a652bb24a4e5048e84d8a1,
ADR0400e10bffed74e55b8be279aa725e2935fb3b4199ede228207329c633c17c0f94f,
plan91fbd4eebecf10b9b29444b347e98f47d2f92feebff9ec339373d874dde0c57f,
Feature7e240ecfe792ff1e08947f8e2a0a19966897fa13970e2ea05e50263d3e345926.
Both disjoint Luna workers are running; partial output is not a completed join.

## Authentic E-run oracle repair and publication join

Run36999295427 at c203860ac4f489debe6a5daad09d8942415e54b6,
job110813053048, immutable qualification artifact11223146701/digest
770e3d37aff799825955f9aba0f91bc3597be3ba3dc87e204f08fdb965c7f2fa:
Analyzer112/112, native thresholds and raw vendor/compression regressions pass.
Site33executed30passed3failed0skipped. Two caller failures are catalog ordering
and display rounding; AfterSession fails because Chrome stops before completion
and its native receipt. Native/site report hashes and full retained failed packet
are recorded in docs/implementation/site-design.json. No website pass or deployment
is inferred from the passing dependency suite.

- [x] TASK019/020 complete source packets inspected and strongest approved; ordinary
  E transport preserved shared HEAD/index and unrelated files.
- [x] E authentic GitHub evidence proves portable vendor root cause repaired.
- [x] TASK026 strongest-approved three-file oracle repair source complete and read.
- [x] Preview catalog caller: real full candidate rerun passes unique IDs, explicit
  large/small/smoke order and every raw/provenance assertion.
- [x] Chrome caller: real full candidate rerun passes shortest-decimal rounding,
  all report/profile/metric/geometry/status assertions and complete browser flow.
- [x] AfterSession: real Node/Chrome receipts satisfy all17 sources and9critical
  modules, unchanged80/70/90 gates, with complete authentic ZIP/raw input checks.
- [x] Join separate owner-authorized AC028 publication workflow and actual main/
  provider/live evidence under site-publication.plan.md before final completion.

## Website qualification and publication closure, 2026-10-02

Exact H validation [37011817610](https://github.com/managedcode/KeyLoad/actions/runs/37011817610), automatic site-path push [37013009381](https://github.com/managedcode/KeyLoad/actions/runs/37013009381) and natural producer-completion callback [37014111869](https://github.com/managedcode/KeyLoad/actions/runs/37014111869) each passed all118 analyzer and70 site tests without skips. This closes the individually tracked source, vendor, diagnostic, argument-role, control/oracle and zero-delta failures above; original failed evidence is retained. Native module, all12 diagnostic pipelines, all17 JavaScript sources and9 critical modules pass unchanged80/70/90 gates. Numeric and artifact receipts are canonical in `docs/implementation/site-design.json`.

The automatic consumer uses genuine successful comparison36926803549, job110586038011 and immutable artifact11193564650, because current producer37013008931 failed. Actual callback event is workflow_run after that same-H producer completion; the versioned trigger names KeyLoad CI, while REST has no triggering-run-id field. This is an enclosing-workflow completion trigger. Failed or invalid evidence never becomes a current measurement. The35-file deployed Pages tar matches qualified output; original12 inputs and all9 public reports are byte-identical.

Authorized Pages provider update returned204: www.keyload.cloud, enforced HTTPS, approved certificate for www/apex. Live www returns200, apex301 redirects to www, and actual HTML, publication receipts, catalog and all9 reports match qualified bytes. Manual live desktop1280/mobile390/320 review shows readable controls/graphs, correct profile/p99/repetition changes and WebGPU scene within geometry/buffer limits, with no captured console warnings/errors. Existing desktop/tablet/no-JS/reduced/coarse/lifecycle evidence and the explicit physically unexercised GPU-loss boundary remain in force. Manual inspection is not test qualification.

One documentation-only descendant may preserve this immutable H as-of record. It must not claim H tests qualify its own SHA; normal CI and Pages automation remain unchanged and any subsequent result is retained in Actions. No self-referential evidence commit is required. Unrelated database, schema3/advanced profiles, endurance/power-loss, broader repository coverage qualification and production-readiness gates remain outside this completed website scope. DNS is unchanged.

- [x] All required bounded source-worker packets terminal COMPLETE and full diff/hash/source joins reviewed.
- [x] Development build/format/static governance separated from actual full GitHub qualification.
- [x] Both automatic triggers, immutable outputs, predeploy freshness, actual Pages provider and live JSON/UI evidence inspected.
- [x] TASK-SITE-REVIEW-007 strongest final integrated acceptance and documentation join.

Website coverage policy, including the root no-decrease requirement, remains mandatory. The three independent exact-H runs retain identical hashed authored sources and2351/2399 covered/executable lines. Their genuine native V8 block inventories are540/626 in validation37011817610,537/623 in push37013009381 and538/624 in callback37014111869; all report86% under the unchanged gate semantics. Native analyzer counts are935/981 lines and528/612 branches in each run. No source, threshold, raw snapshot or outcome is removed to make these counts agree; all17 authored sources,9 critical JavaScript modules and12 analyzer pipelines remain mandatory. This is the first configured qualified website baseline, with broader repository coverage separately unqualified.
