# KeyLoad site design acceptance

Date: 2026-10-02. Scope: `BenchmarkComparisons`. Owner instruction authorizes a proper product website design and Three.js. Strongest planning: TASK-SITE-PLAN-001, gpt-6-astra ultra, COMPLETE before implementation. [Brainstorm](site-design.brainstorm.md), [feature](docs/Features/BenchmarkComparisons.md), [ADR-040](docs/ADR/ADR-040-static-site-threejs-evidence.md).

## Outcome, scope and actors

Readers understand the experimental .NET data engine before inspecting its benchmark evidence. A composed responsive page contains product capabilities, a labeled conceptual RF3 illustration, the full historical benchmark explorer, methodology, guarantees and source/docs links. Three.js is decoration supporting the architectural explanation; it is never live telemetry or performance data.

In scope: product-first HTML/CSS, Three.js 0.186.1, canonical feature-asset migration, atomic validated report loading, unchanged schema2 arithmetic, isolated clean static builds, a separately buildable TUnit site suite, source/static/manual-browser evidence. Out of scope: database/runtime/API/persisted-format changes, new benchmarks, widened schema3 publication, DNS, public deployment, global tool/skill installation. Backend and persistence surfaces are N/A because this is a static evidence presentation slice.

Actors: reader, keyboard/screen-reader user, constrained/reduced-motion browser, evidence publisher, bounded coding workers and root integrator. Entry: generated index → independent benchmark/scene mounts → same-origin assets and verified report files. No authentication secret, backend, external CDN, third-party font request or browser credential is introduced.

## Stable requirements and measurable criteria

| Requirement | Acceptance and failure boundary |
|---|---|
| REQ-BC-011: product-first responsive design | AC-BC-011: at 1440×900, 768px, 390×844 and 320px the first render gives product purpose, honest qualification and usable navigation. No clipped controls or page-level horizontal overflow. A labelled data-table scroll region is allowed. |
| REQ-BC-012: bounded conceptual Three.js RF3 scene | AC-BC-012: pinned WebGPURenderer renders three physical hosts and logically separate partitions. Record actual native backend. Init/cancel/resize/pause/resume/dispose/navigation restoration leave at most one canvas, renderer and active frame request; zero-size hosts do not allocate invalid buffers. DPR ≤1.5, buffer ≤1M pixels, ≤30 draw calls, ≤5K triangles, pointer motion settles ≤500ms, no idle loop. |
| REQ-BC-013: accessible independently readable content | AC-BC-013: semantic heading/nav/figure/chart/table and visible focus; original roving scenario tabs and labels retained. Reduced/coarse-pointer motion stays static. No-JS, unavailable graphics, init error or context loss leaves readable product text, architectural poster and source/evidence access. Decoration never captures essential scroll/navigation. |
| REQ-BC-014: exact historical numerical semantics | AC-BC-014: six scenarios, eleven metrics, profile, median/individual repetition, log scale, min/max whiskers, table and JSON/CSV/Markdown downloads remain functional. Production JS matches an independent C# oracle over authentic successful GitHub reports; unsupported/not-recorded/failed values are not fabricated zeros. Preserve median of per-run percentiles, aggregate failure ratio and generator resource labels. |
| REQ-BC-015: atomic validated report state | AC-BC-015: clear/disable every report-dependent surface on loading or failure; abort previous fetch and reject stale generations. Malformed data, bad paths, mismatched raw hashes and rapid profile changes cannot mix labels, data, provenance or downloads. Accept only confined same-origin report paths and allowlisted repository GitHub run URLs. Render text safely, without raw HTML injection. |
| REQ-BC-016: canonical complete asset/build migration | AC-BC-016: behavior/styles/template under site/Features/BenchmarkComparisons; shared build entry/favicon remain outside. Replaced flat files are removed after joined proof. Verify official vendor integrity/license/file hashes, copy all dependencies, preserve raw report bytes, generate an isolated clean output with no stale assets. Authored critical JS ≤40KiB gzip and CSS ≤20KiB gzip; record vendor size separately and lazy-load after baseline content. |
| REQ-BC-017: real TUnit GitHub site qualification | AC-BC-017: KeyLoad.SiteTests uses existing centrally pinned TUnit/MTP/.NET10 and BCL, no Core dependency or second runner. Real bounded Node child processes import actual production modules. Preserve old median/repetition/failure/unsupported/order intentions, add acceptance-driven malformed/path/hash/build regressions. Execute only in GitHub Actions; retain exact source/run/job/artifacts. Visual/lifecycle manual exception requires the browser packet below. Unconfigured numeric coverage remains unqualified. |
| REQ-BC-018: explicit provenance and delivery boundaries | AC-BC-018: site revision, measured revision, successful run and raw hashes remain distinct. Historical schema2 never qualifies schema3/nine-engine/replicated results, current source performance, power-loss or readiness. Local preview is not publication, DNS or a CI result. CLI consultation records actual model; an alias is not proof of Opus5.5. |
| REQ-BC-024: required numeric site coverage | AC-BC-024: exact-source GitHub tests collect normal Node and actual-browser precise V8 coverage for every authored production module. Required aggregate line coverage≥80%, available V8 block-branch coverage≥70%; critical measurement/validation/build/provenance files each≥90% lines. Missing files count uncovered and fail. Retain native ranges, immutable source hashes, runtime versions, reporting semantics and first baseline. No vendor/test/assets denominator inflation, ignore pragmas, dropped files or threshold reduction. |
| REQ-BC-025: actual browser qualification in GitHub | AC-BC-025: real system Chrome and BCL CDP/WebSocket load real builder-emitted authentic reports through confined HTTP. Verify controls/table/bar/provenance/download correspondence, keyboard, rapid/cancel/error/retry, responsive/no-JS/reduced-motion behavior and scene init/idle/pause/navigation/disposal. Record actual backend and runtime; missing browser or required coverage fails without installing a substitute. Browser measurements cannot claim hardware device-loss qualification. |
| REQ-BC-027: qualified analyzer dependency | AC-BC-027: complete real AnalyzerTests plus native MTP18.11.2 Cobertura at candidate SHA; all30 source files/configuration frozen before/after, all25 executable files present, module≥80% lines/≥70% branches and each of12 critical diagnostic pipelines≥90% lines from unrounded integer counts. Missing/malformed/ambiguous/changed evidence fails. Pure gate regressions cover exact boundaries and failure cases in GitHub; raw XML and derived report are retained. This establishes only the first Analyzer-module baseline. |

## Test and review matrix before coding

| AC | Automated acceptance / level | Required manual evidence |
|---|---|---|
| 011/013 | TUnit static-build contracts verify complete semantic hooks, asset resolution and no measured constants in scene/copy | Real desktop/tablet/mobile first render, keyboard tab focus, native selects, readable chart/table, scroll and no-JS content. Visual quality cannot be decided by string assertions. |
| 012/013 | TUnit build/vendor contracts prove pinned distribution, independent mounts and complete scene dependency output | Real browser backend, buffer/draw/triangle counts, pointer settle, resize/zero-size, pause/reduced-motion/hidden/offscreen, cancellation/disposal and page navigation. No simulated device.destroy claim: intentional destruction is ignored by pinned public loss handling. Physically unexercised GPU loss stays unverified. |
| 014 | SiteMeasurementTests plus independent SiteMeasurementOracle check actual production JS for all historical profiles/scenarios/targets/repetitions/metrics; odd/even/empty and failure-edge inputs | Select every scenario/metric/profile/repetition/log control; inspect table/bar/whisker/download correspondence on genuine GitHub evidence. |
| 015 | SiteEvidenceValidationTests exercise actual validators and corrupt/path/hash/revision cases; no fake service or alternate parser | Rapid profile changes, report/catalog failure, restored healthy profile and console/network observations. |
| 016 | SiteBuildTests run real builder into disposable output, verify raw byte hashes, vendor assets, isolated clean output and rejection cases | Inspect generated static site, asset network requests and actual transfer budgets. |
| 017/018 | GitHub-only full site TUnit suite and exact SHA/run/artifact receipt; scoped static/governance/source review | Strongest final full-diff review joins every COMPLETE packet. Explain broader source/coverage/publication gates separately. |
| 024 | Native precise V8 data from actual Node/browser processes, source-matched converter regressions (nested/uncalled/differing partitions/Unicode/CRLF/missing/hash/bounds), and after-session threshold gate | Strongest semantic review of converter/denominator; manual observations do not satisfy coverage. |
| 025 | Real TUnit/Chrome integration against generated files; no fake dependencies or another runner; known source/process/coverage receipts retained | Product composition and physical-device judgment remain explicit manual review. |
| 027 | Full AnalyzerTests; SiteAnalyzerCoverage-prefixed real PowerShell-process TUnit parser/threshold regressions; pre/post source inventory and post-process raw Cobertura gate | Strongest pipeline/denominator review; no claim of product/server coverage or historical no-decrease without matched evidence. |

The manual exception is for design judgment and real browser/device lifecycle, not permission for local TUnit/test-runner/load execution. Local preview builds and browser inspection are source/design-review artifacts only. The full relevant site suite must still run in GitHub; unfinished product CI/coverage cannot be described as passed.

## Baseline, migration and consultation

Baseline evidence: successful GitHub run36926803549 at9c570f8c33a7a9667507a8e1c0ca68860de3be45 with its authentic three schema2 reports. Initial preview8787 serves older67c5bbc evidence and is not the new design proof. Current source has unrelated unqualified Core/analyzer/migration work; preserve it. No existing failing site TUnit test is known because this suite is newly introduced; current Node test debt is migrated without losing assertions.

Claude CLI2.1.267 consultation used alias opus, actual returned model claude-opus-5, no tools or writes. Exact claude-opus-5-5 returned API400 requiring CLI2.1.280+. Keep useful design input; reject its stale Three0.169 advice, false model-availability statement, partial malformed report display and changed log arithmetic. No CLI update is part of this task.

Migration removes superseded flat site behavior after replacements are joined; no runtime/data migration. Rollback restores the prior coherent site source/assets without deleting immutable benchmark evidence or weakening mandatory policies. Public Pages remains bound to successful source evidence; any future site/measured two-revision publication contract requires separate review.

## AC-BC-027 observed GitHub failure contract

Run36988549282 atb402dc50785028cc5b09a3928c5b28ba8a721a6e executed every88
AnalyzerTests with84 successes,4 failures and0 skips. This observed baseline adds
the following required repairs under the existing BC-027/CQ-006/CQ-008/009 IDs;
it does not reduce the native80/70/90 or complete-suite acceptance conditions.

- Native condition-coverage display accepts the observed integer and1–2decimal
  percent spellings in0..100. Strict delimiters and unsigned integer counts remain
  mandatory; reject missing/comma/sign/exponent/out-of-range/malformed display.
  The covered/valid integer pair alone controls thresholds. A displayed70.00%
  with69995/100000 must still fail70%; native16.67%,56.25%,62.5%,83.33%,91.67%
  reports must parse into their unchanged exact integer counters.
- Numeric diagnostic spans start at the declaration's first token, excluding
  leading indentation while retaining the complete header/body end. Three
  observed failures must retain exact ID/error/path/line2/column4/end assertions
  plus depth2/3/4,49/50/51 and disabled-text scenarios. The independent expected
  span uses the known source signature, never the actual diagnostic or analyzer
  location helper as its oracle.
- Every existing test remains registered exactly once. Cohesive test-class splits
  must satisfy the real self-inventory200type/400file/50unit/3depth limits, without
  partial aggregation, source exclusion, suppressions or reduced assertions.
  Strongest source review additionally found the existing ElseIf/loop/using/fixed
  method's raw-string token makes its full executable unit exceed50code lines.
  Extract only that unchanged compiler-input literal to a named private constant
  in its owned extended-construct class. Preserve its exact decoded source bytes,
  all assertions and the same registered case; do not alter the numeric counter.
- Required KLD0001 and KLD0022 diagnostic-flow gaps receive meaningful real SDK
  Roslyn inputs with caller-visible positive/negative/edge assertions before the
  retry. Keep all25 executable sources and12 critical-pipeline denominators; no
  padding, direct helper invocation for counts or reclassification is allowed.

Verification mapping: SiteAnalyzerCoverageBranchDisplayTests runs the real
PowerShell verifier through the existing TUnit process scope; the original three
numeric cases and full NumericAnalyzerSelfInventoryTests prove span/cohesion
repairs; required diagnostic-flow regressions exercise real analyzers and actual
BCL/Orleans metadata. GitHub alone executes the full AnalyzerTests/native gate
and subsequent SiteTests suite. Root joins all changed sources and strongest
review before an ordinary descendant candidate retry; preserve failed TRX/XML
and every exact source/run/job/artifact receipt. ADR-033 owns implementation and
rollback; no runtime/public/data contract changes occur.

TASK016's frozen semantic map uses four new CodeQuality test files only:
LiteralMachineKeyInvocationTests.cs, LiteralMachineKeyConstructionTests.cs,
LiteralMachineKeyAttributeContractTests.cs and SystemClockSemanticTests.cs.
Real Dictionary.Add positional/named keys, NameValueCollection.Add key/value,
JsonElement.GetProperty with direct/nested string literal, dictionary complex
initializer and KeyValuePair positional/named key are machine-key positives.
Named constants, empty strings, List.Add human text, ordinary Tuple constructor
strings and normal value positions are negatives. Actual DataMember(Name),
fully/global-qualified JsonPropertyName and JsonPolymorphic discriminator keys
are positives; Description human text and constant metadata are negatives.
Clock positives cover DateTime Now/Today/UtcNow and DateTimeOffset Now/UtcNow
including aliases/static imports; TimeProvider.GetUtcNow, static nonclock date
members and instance date properties are negatives. Each positive checks exact
count/ID/error/path/source token location; each negative requires no diagnostic.
Use the existing AnalyzerFixture's compiler-valid real platform metadata.

Reordered named dictionary values or Utf8JsonWriter.WriteString value positions
must not be asserted as machine keys to match a false positive. If source review
or GitHub exposes this issue, retain the meaningful failing regression, report
the exact owning production path and stop/escalate before any production edit.
Plain interpolation text is outside the current literal registration; use the
actual nested literal `GetProperty("prefix" + suffix)` for that accepted edge.

## TASK018 machine-key argument-role regression and repair

REQ-CQ-002/006, AC-CQ-004/008/009 and REQ/AC-BC-027 require a meaningful fix
for TASK016's retained real `Utf8JsonWriter.WriteString` regression. That task is
blocked until this scoped repair is joined; its partial output cannot satisfy017.

Pass: actual named/positional/expanded arguments resolve to their parameter. An
explicit key parameter or the existing heuristic's first logical parameter is a
key; a known value parameter never becomes a key because it appears first in
source. Ignore the receiver of unreduced static extension calls; reduced calls
already omit it. Match expanded final `params` elements to the same parameter.
Keep nested key expressions positive and nested human value expressions negative.
For unresolved/dynamic invocations preserve method-name fallback restricted to
recognized named keys or first unnamed syntax argument; unresolved constructors
retain their non-key result. Catalog/ID/severity/generated/attribute/indexer/
initializer/empty-string behavior remains unchanged.

Tests-first ownership: only new LiteralMachineKeyArgumentBindingTests.cs and
LiteralMachineKeyCreationBindingTests.cs. Use real SDK Roslyn/BCL/ASP.NET metadata
for positional/reordered WriteString and Dictionary.Add, named constant key plus
literal value, nested key/value concatenation, reordered KeyValuePair and
DictionaryEntry, ordinary List/Tuple negatives, reduced/static WithName/WithTags,
expanded params and compiler-valid dynamic invocation fallback. Every result
asserts exact diagnostic count/ID/Error/path and independent full literal span.
An unsupported SDK shape escalates; no framework stubs or fixture weakening.

GitHub must run the real tests-first source cut with production unchanged and
retain its failures. This expected red run is a regression baseline, never017
acceptance. Only after root/strongest review of that receipt may the worker edit
MachineKeyLiteralClassifier.cs and MachineKeySemanticSymbols.cs using one shared
argument-role decision. Then join source/build/format, resume016's four-file
semantic map, hash all unchanged25source paths with new content and run complete
AnalyzerTests/native coverage/site suite. Actual new denominators determine90%,
never historical224/30 or rounded summaries. No local tests, exclusion, threshold
change or shared fixture/configuration/package modification.

## Observed SiteTests portability and diagnostic repair contract

REQ/AC-BC-016/017/024/025 retain their full requirements. The genuine failing
positive builder and browser tests in run36994330874 are the regression baseline;
write the following additional assertions before the corresponding source repair.

AC-BC-016 portability passes only when the real builder accepts an isolated copy
of its unchanged production source/vendor and all three authentic GitHub reports
after changing solely valid historical gzip metadata. Every recorded gzipBytes
must be a positive safe integer. Exact raw file SHA256, byte length, package,
version, MIT license, sourceCommit, integrity and complete file identities remain
mandatory. Wrong metadata identities, invalid historical gzip values, raw byte
corruption, recorded hash or byte-length corruption reject with the existing
vendor error before output creation. Official repository vendor files/manifest
are immutable. Copied production files are real isolated inputs, never doubles;
they cannot substitute for exact-source production coverage.

The successful builder CLI JSON adds `compression` with actual `nodeVersion`,
`zlibVersion` and `vendor` entries containing `path`, `sha256`, `bytes`,
`recordedGzipBytes` and `runtimeGzipBytes`. The latter is computed from those actual
bytes by the running Node zlib; do not infer the hosted failed run's missing size.
Existing output/profile/authored-JS/CSS fields, raw-report preservation and40KiB/
20KiB budgets stay unchanged. Real TUnit assertions verify current gzip against
independent Node gzip output, preserved source identity and unchanged budgets.

AC-BC-017 diagnostic passes only when each completed real builder process retains
bounded stdout/stderr and its exact exit status in a unique directory under
`artifacts/site-evidence/builder-invocations`, outside JS production coverage
inputs. `invocation.json` schemaVersion1 records siteSourceRevision,
measuredSourceRevision, evidenceRun, evidenceUrl, workingDirectory, arguments,
builderSources (relative path and raw SHA256 for the entry and feature builder),
exitCode, stdoutFile and stderrFile. Files are `stdout.txt`/`stderr.txt`; no process
environment, credentials or fabricated runtime output. Preserve RunAsync's
signature/result, all output bounds, timeout/cancellation, pipe draining and
owned-process/temp cleanup. The record survives temporary teardown; an evidence
write failure cannot become a passing process result.

Browser pre-build failure must identify the builder and include its actual exit/
bounded stderr; it must not claim Chrome launched. Existing registered negative
tests keep every sentinel/no-residue assertion and additionally identify the
specific output-path or symlink error. The malformed JSON case must identify a
JSON parser rejection and exclude the vendor error. Existing validator/HTTP
tests keep their real accepted-build and rejected-report assertions. Additional
real-builder diagnostics tests verify success/failure receipts and provenance;
no browser/process stub or local qualification is allowed.

Verification map: TASK019 owns the builder plus new SiteVendorBuildTests,
SiteVendorTestScope and SiteVendorTokens only; TASK020 owns SiteBuildSupport,
SiteBrowserSession, SiteBuildTests and SiteEvidenceValidatorTests plus new
SiteBuilderDiagnostics/SiteBuilderDiagnosticsTests/SiteBuilderTokens only.
Strongest recorded-contract approval precedes both disjoint write tasks. Root
joins every source/assertion/receipt diff, development build/format/static checks,
then complete GitHub Analyzer/native/site/real-Chrome/JS80/70/90 qualification.
No gate, registration, denominator or original failure record is weakened.

E-run36999295427 exposes AC-BC-016/025 test-oracle defects under TASK026. Catalog assertions MUST retain exact profile count, uniqueness, per-ID provenance/hashes/timestamps, and explicit production order [LargeProfile,SmallProfile,SmokeProfile]. Browser display oracle MUST use shortest round-trip decimal (R invariant) then decimal.Parse(Float invariant), decimal.Round(2,AwayFromZero), then existing en-US format. Preserve null marker, numeric row calculation and every exact UI/geometry/status assertion. Named formatter regressions cover actual below-half0.47499999999999964, exact0.475,1.005,just-below1.005,zero,0.005,1234.5,null; controlled formatter correctness values never become published performance. Full real Chrome completion and native coverage remain mandatory.
