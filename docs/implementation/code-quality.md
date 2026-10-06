# CodeQuality evidence

Configuration and rule contract: [CodeQuality](../Features/CodeQuality.md).
Implementation decision: [ADR-033](../ADR/ADR-033-code-quality.md).
Working verification: [plan](../Features/CodeQuality.md).

## Executable-unit64 development checkpoint, 2026-10-06

TASK-CQ-UNIT64-010 implements the explicit owner correction in REQ-CQ-006 /
AC-CQ-008: KLD0032 now accepts64 counted code lines and rejects65. File400,
aggregate type200, nesting3, Error severity, generated-source exclusion, counting
and exact diagnostic spans remain unchanged. Active root/local policy, feature
contracts and ADR-033 use64; historical receipts retain their original limits.

The local macOS arm64 R72 canonical complete Release build passed with0 warnings
and0 errors and no changes among4221 captured compile inputs. Original build log
SHA256: `e1f842aff8492c3c95f2758d2918f4ebf3ba57c9650a05bce82b27adc1e8532f`.
The actual Aspire-owned complete `analyzers` suite executed388/388 cases,
all passing; original TRX SHA256:
`a42d04102deff97fc6b83fe8099ca315270202a9a2962780c893987db2863e42`.
Its before/after guards found no drift among4272 source inputs or84 DLL/PDB
inputs. Exact63/64/65 compiler outcomes, precise Error/message/span, oversized
accessor/lambda/nonmethod bodies, raw strings, disabled text, comments and
Roslyn generated-code handling all executed. Two actual initial CA1305 findings
were repaired with invariant diagnostic formatting; no analyzer was suppressed.

The same source cohort's genuine cancellation and inclusive stdout boundary
flows passed3/3 through Aspire, including rejected output plus healthy following
child operations. Original TRX SHA256:
`78be0c61611cac07a0a4168220a69f299771c33e701c290584247d391baf3707`.
Canonical complete `dotnet format KeyLoad.slnx --verify-no-changes --no-restore`
returned0. Repository governance and whitespace checks also passed.

These are bounded local development results. The separate unchanged native
Orleans telemetry flow executed1 case and failed its late-baggage privacy oracle;
original failed TRX SHA256:
`7c93d4056304cb30d910ff4df7052d23792df794ed952dfbb75a5c7e2368a534`.
The original c8 push CI37450680516 failed full unit/scalar/recovery and RF3 source
preparation; no RF3 case executed after that preparation failure. Global
functional coverage, CRAP and delivered-source Linux acceptance remain
unqualified. No original104 task or overall CodeQuality acceptance is closed by
this checkpoint. At that checkpoint, pending bounded cleanup and prior-format
composition packets had not been integrated and were not evidence of passing
operations.

## Native cleanup development checkpoint R75, 2026-10-06

The R75 full canonical Release build passed with0 warnings/errors,4224 compile
inputs and no source drift; original log SHA256:
`29429ce51f04b68aa928ef237928865dd33542228f7436643608383442820b4d`.
The actual Aspire-owned cleanup/source-manifest flow executed13/13 cases, all
passing in1248.339 seconds. Original TRX SHA256:
`e75ac3954550629ae3cefca18bb5678c6dad5d707e108fcdb3282152e86e64ef`;
original caller log SHA256:
`9c66eecf271defebb2b709b81a88cc126ef9a538de6c7e050ad5fed0db0022c1`.
The4275 source and84 DLL/PDB before/after guards found no drift. Actual child
cancellation and inclusive/overflow pipe boundaries, joined original tasks,
healthy source preparation, tamper rejection, preserved evidence and healthy
following operations ran. Forced fatal-deadline expiry, complete suites, functional
coverage and exact-source Linux gates remain pending.

The unchanged complete R75 native telemetry case failed1/1; original TRX SHA256:
`d5c6d5193a8300bc85e6117836208de75176490acb4a1e2c833e82593f79ce1a`.
The bounded diagnostic proves actual Error status remained Error; every selected
native span inherited one ambient baggage item and the privacy processor suppressed
its recording. The new independent caller-context correction is source joined
with all whole-operation privacy, state, identity, parentage and metric assertions
retained; its native verification is pending.

The subsequent R76 source cohort added the first-write partition roster and
caller-context correction. Its full build failed6 errors with0 warnings and
no source drift: three CA1859 concrete-type corrections, two KLD0037 native
options-boundary findings and one KLD0035 unnamed empty-count value. Original
log SHA256:`a0311da1e914452a4b0495426d6ab18cae0d1fa73974d7a09f5cf569ba7deec2`.
This failure is retained; focused tests cannot be admitted using stale binaries.
The scoped preserving correction and fresh native gates follow. No whole-task,
cluster backup, global coverage or Linux acceptance closure is claimed.

## Historical initial analyzer baseline, 2026-10-02

The owner-selected Prostir EditorConfig was imported byte for byte on 2026-10-01;
the final byte comparison passed on 2026-10-02. SHA256:
`b624db78d435290ba2633a71c901797ff3d66449f76ffd91b4b81724b379b6fb`.
No imported disabled severity was changed; no authored suppression was added.
Twelve rules are source-owned in the CodeQuality slice, with 57 authored TUnit
test methods using real compiler, Orleans and ASP.NET metadata. Four new rules
enforce file400, aggregate partial type200, executable unit50 and nesting3 limits
under the precise metric and bounded exception contract in ADR-033. These authored
tests have compiled but have not executed for this source.

## Functional coverage development checkpoint, 2026-10-06

The current implementation collects four functional profiles through Aspire after
one Release build, with the complete required suites retained separately. Load,
stress, performance and comparison contributors are excluded. CI preserves raw
coverage, TRX, TUnit and RF3 process receipts before descriptor/merge admission.
The production roster is14 modules; the canonical source inventory additionally
classifies AppHost and Analyzers as infrastructure. Global coverage remains
unqualified while contributor/module and native Linux RF3 evidence is incomplete.

The local macOS arm64 R42 run executed all three `CliBackupRestoreFlowTests`
successfully through Aspire: backup/pack/restore of a committed ZoneTree store,
rejection of a nonempty destination with both stores preserved, and invalid-budget
rejection followed by healthy restore. Original TRX SHA256 is
`fdd1723677de1c26f3c6415025246e7922fbed4a8428ac121e8da57565e27d50`;
original native binary coverage SHA256 is
`64b88c64e6d8e651a76bd8b4fa13726330388f16e8cd984333332d9b1ac13b10`.
The pinned18.11.2 native export reported1007/36703 raw lines across nine modules,
with CLI/Artifacts child modules absent. These scoped host counts cannot qualify
the complete roster or become a whole-solution percentage. Explicit child-process
collection and supported dynamic managed instrumentation are now configured;
fresh child hits and source-bound Linux qualification remain required.

The native Cobertura export omits both integer branch attributes and contains no
branch outcomes. Its `branch-rate="1"` is not100% proof. Native readers preserve
unavailable aggregate counts as null and merged branches as unmeasured. The
complete backup/restore/native-merge regression remains pending after the child
lifetime and native-schema fixes; earlier failures are retained as failures.
No load measurement, CRAP result, RF3 coverage percentage or acceptance closure is
claimed from this development checkpoint.

The canonical formatter, whitespace check and repository-governance inventory
passed locally before the final native-schema join. Required final build, focused
runtime checks, complete Linux suites, coverage and fault/endurance gates remain
independent of this source checkpoint.

## Observed gates on 2026-10-02

| Gate | Actual evidence | Result |
|---|---|---|
| Current joined working tree on 2026-10-02, based on `9c570f8c33a7a9667507a8e1c0ca68860de3be45` | `dotnet build KeyLoad.slnx --no-restore --configuration Release` | Passed all 25 projects with 0 warnings and 0 errors. This is a local, uncommitted source build; no test qualification is implied. |
| Current joined solution formatter and governance | `dotnet format KeyLoad.slnx --verify-no-changes --no-restore`; `node scripts/Features/RepositoryGovernance/verify.mjs`; `git diff --check`; implementation JSON parsing | Passed; all 25 project policies and 4 modules preserved. No TUnit, process-recovery, container, RF3, MCP or benchmark workflow ran for this working tree. |
| Restore | `dotnet restore KeyLoad.slnx` after the embedded library and central CSharp/Common5.0 peer join; strict-embedded-benchmark-paired-restore.log, all25 projects, no new locks | Passed development prerequisite; generated Dry compatibility remains pending |
| Central policy | Actual Release MSBuild evaluation of all25 projects, configuration-20261002-25-projects.json;23 ordinary consumers attach the custom analyzer, its own two projects avoid recursion | Passed static check, no policy mismatches |
| Negative policy | Build with `-p:RunAnalyzers=false` fails at EnforceKeyLoadCodeQuality before compilation | Expected rejection passed |
| Analyzer and test project build | Release Rebuild of KeyLoad.Analyzers.Tests also builds KeyLoad.Analyzers | Passed, zero errors/warnings; not test execution |
| Initial solution build | Release build with all analyzers enabled before prerequisite repairs | Failed: 1180 errors, zero warnings; historical source stage |
| Historical lead solution build | Dependency-enabled Release build after the shared Core/contract join | Failed: 459 errors, zero warnings; source cut before Query/Artifacts repairs and site restore |
| Pinned-driver benchmark join | Normal dependency-enabled benchmark Release build after Kurrent/Mongo API repairs | Failed: 506 analysis/style/XML errors, zero warnings and no type/API-binding errors; historical source cut before final private disposal/type fixes and restored public Kurrent visibility |
| Historical comparison dependency build | Normal dependency-enabled Release build after adapter repairs, before immutable/numeric joins | Failed: 16 library diagnostics, zero warnings; nine CA1819, CA1720, CA1710, two CA1032 and three CA2100; the library failure prevented host compilation |
| Historical prerequisite builds | Normal dependency-enabled Release builds of Abstractions, Core, ZoneTree, Client, Query and Artifacts before numeric rules | Passed, zero errors/warnings at that source stage; not current numeric or runtime proof |
| Historical full solution numeric source cut | Ordinary enabled Release build, strict-solution-numeric-inventory.log and inventory JSON | Failed: 49 errors, zero warnings across ZoneTree6/Core4/Client3/SiteTests36 at that source stage |
| Historical private storage full solution source cut | Ordinary enabled Release build, strict-solution-private-storage-join.log | Failed: 54 errors, zero warnings across SiteTests36/CLI13/Query3/Security1/Comparisons1; failed dependencies leave further projects uncompiled |
| Most recent whole-solution build before embedded/query/storage-test joins | Ordinary enabled Release build, strict-solution-host-site-join.log and deduplicated inventory JSON | Failed:19 errors, zero warnings across Benchmarks14/CrashHost2/AppHost2/UnitTests1 at that source stage; nonexhaustive historical prerequisite cut, no current whole-solution success |
| Reviewed numeric prerequisite joins | Normal Abstractions/Core/Client/Storage/Security/Query/CLI/Comparisons builds after reviewed private-owner and guard joins | Passed, zero errors/warnings with numeric rules enabled; no test execution |
| Bounded storage metadata production join | Normal enabled Storage Release build, strict-storage-bounded-metadata.log SHA256 e8cdfdabeeb8c442095a0d1be61aa2f261adf6bde209b6caccd2c239151e6217 | Passed0errors/0warnings; corrected test-source review/registration/allocation/full GitHub qualification pending |
| Historical comparative host source cut | Normal real-dependency host Release build, strict-comparison-host-numeric-owner-join.log | Failed: eight ownership/cleanup diagnostics, zero warnings; subsequent preserving repair reviewed |
| Reviewed Host and SiteTests joins | Normal enabled Release builds, strict-comparison-host-cleanup-final.log and strict-site-process-quality-final.log | Passed, zero errors/warnings; real cleanup/default/options/lifetime regressions authored, execution pending |
| Prepared storage mutation source join | Normal enabled Release build, strict-storage-prepared-mutations-join.log | Passed, zero errors/warnings; one private sorted mutation projection reused for validation/serialization/apply, three real-store regression sources pending CI |
| Reviewed Host/Site/prepared source formatter | Canonical solution formatter with only these joined source paths included, formatter-host-site-prepared-join.log | Passed exit0; scoped static proof, complete solution formatter and test execution remain pending |
| Reviewed embedded scenario library/host | Normal enabled Release build, strict-embedded-benchmark-host-final.log | Passed0errors/0warnings; actual generated Dry child/TUnit execution pending |
| Historical UnitTests dependency graph before final lifetime/formatter joins | Normal enabled Release build, strict-unit-query-storage-embedded-corrected.log and deduplicated inventory JSON | Failed114errors/0warnings at that source cut; post-build snapshots, no runtime qualification |
| Historical UnitTests dependency build after reviewed lifetime/formatter/alias joins | Normal enabled Release build, strict-unit-source-quality-final-join.log SHA256 f7fb4cb72fc94f75ac38018b41bff8ee8ec42bdb0202b78da18fcd4c825ea156 | Failed1error/0warnings at that source cut: protected concurrent Core PersistCommandOutcome CA1822. ArtifactTransfer compiled with its original provider CLR type; UnitTests Csc did not run |
| Latest UnitTests build after pure harness migration | Normal enabled Release build, strict-unit-pure-harness-source-join.log SHA256 71e19d7bde4a76ac9fbbc7a83bfeca8eeebfb0600caf7a749abd35e15fa86152 and deduplicated inventory | Reached UnitTests Csc, failed3errors/0warnings in protected concurrent BlobRestoreNormalizationTests missing DatabaseEngine. Core's previous CA1822 cleared; all this build's dependencies compiled. UnitTests combined semantic compilation/registration and runtime remain pending |
| Comparison-only friend metadata | Normal enabled Release library build, strict-comparisons-friend-source-join.log | Passed0errors/0warnings with the single named ComparisonTests friend; parser/target/genuine runtime joins pending |
| Reviewed unit formatter join | Canonical solution formatter verify with UnitTests include, excluding protected BlobStorage/ClusterRouting/ClientApi and separately pending BenchmarkComparisons/ComparisonHarnessTests; formatter-unit-owned-source-final-join.log | Passed exit0 as scoped static evidence; full canonical formatter and actual UnitTests compilation/runtime remain pending |
| Formatter | Canonical solution verify-no-changes | Failed on existing source/style findings |
| Scoped formatter | Same command including only the two new analyzer project trees | Passed |
| JSON text source/test formatter | Same command including the pooled JSON/shared callers and four new test files | Passed after removing two unused imports; no runtime test execution |
| Latest governance | `node scripts/Features/RepositoryGovernance/verify.mjs` after the embedded library joined | Passed: root policy preserved,25 projects, four modules, local policies and empty installed-skill catalog |
| Whitespace / import review | `git diff --check`, byte comparison and lead joined-diff review | Passed static checks |
| New analyzer tests and full product suites | Exact delivered-SHA GitHub Actions qualification | Pending; these new tests have not executed |
| Numeric complexity | Four source-owned rules configured and compiled; real compiler fixtures and complete graph qualification required | Configured, full gate pending; no weakened thresholds or new exceptions |
| Numeric coverage | Compatible collector, container export, strict raw-report verifier and real baseline | Not configured; cannot claim passing |

Generated TUnit registration sources confirm the new methods were discovered
during compilation. That is source-generation evidence, not a regression result.
No tests, recovery qualifications or load benchmarks ran locally.

The initial solution errors came from Abstractions and ServiceDefaults:
1078 CS1591 missing public XML comments, 49 CA1819 array properties, 22 KLD0001
literal machine keys, 21 IDE0011 missing braces, three CA1032 exception-constructor
findings, three CA1062 validation findings, two CA1307 string comparisons and one
each of CA1716/CA1724 public naming findings. Their failed dependency graph leaves
18 further projects uncompiled; this is not a complete source-debt inventory.
ServiceDefaults and contract XML prerequisites have since been repaired. The latest
lead solution build at the contract-documentation stage had 50 CS1591, 49 CA1819, one CA1062 and one CA1716 error in
Abstractions. Storage contract XML and argument guards were then authored; a normal
worker dependency build reached the remaining 49 CA1819 and one CA1716 diagnostic.
ADR-041 now governs the explicit read-only CLR contract/consumer migration with
stable wire bytes and strict null/default rejection. Abstractions and its joined
Core, Query, ZoneTree and Client consumers compiled with their real dependencies
before the numeric-rule stage.
The subsequent solution build reached 72 Replication, 361 Comparisons, 20 Artifacts,
five Query and one missing site-assets error. Query and Artifacts were then repaired
and rebuilt cleanly; restore supplied the site assets. Replication and comparison
source are being integrated with their concurrent owners. These counts identify
that exact earlier build, not a fresh remaining-error total or runtime result.

An earlier comparison-library source cut contained 62 CS1591, 16 CA1062, 13 KLD0001,
nine CA1819, six CA2234, three CA2100, two each CA1032/CA2002/KLD0024/IDE0290 and
one each CA1720/CA1710/KLD0022/CA1068/CA1849/IDE0019. Shared corpus XML and the
preserving sole-host/library split are source-joined. Adapter XML/private helpers,
shared guards and source review proceed under AC-CQ-007; public collection/enum/
exception and SQL designs now have separate accepted implementation contracts. Counts are from
that exact build, not a fresh complete solution inventory. The IDE0011 join retained
all 22,210 non-brace tokens in its 28-file cut; this does not establish compliance
with remaining file/type/function limits or runtime correctness.

Compiler SARIF 2.1 reports contain real rule IDs, messages and source locations.
The initial report snapshot included 88 informational style findings and 33 CA1822 records
suppressed by TUnit's standard instance-test-method suppressor; those suppressed
records do not count as build errors. Per-project paths avoid collisions. CI is
configured to upload diagnostics even when builds fail and to qualify analyzer
rules independently of the product graph.

Local ignored evidence: `artifacts/code-quality/report.md`, `summary.json`,
`configuration.json`, `solution-build.log`, `analyzer-build.log`, `formatter.log`,
`disabled-analyzers-rejected.log`, `configuration-20261002-24-projects.json` and
per-project `Release/net10.0/diagnostics.sarif`. New joined build/inventory logs are
retained under `/private/tmp/keyload-performance-baseline/`; the complete solution
source cut is `strict-solution-after-core-join.log` and its inventory JSON. The
later document-image Core join is `strict-core-document-image-join.log`.
The pooled JSON/replay Core join is `strict-core-pooled-json-join.log`; the historical
host dependency cut after braces is `strict-comparison-host-after-braces.log` with its inventory.
After preserving U/V/X repairs the actual normal dependency cut
`strict-comparison-preserving-join.log` contains 41 errors, zero warnings. After
reviewed Y HTTP/native guard and ownership packets,
`strict-comparison-adapter-join.log` contains 16 errors, zero warnings: nine CA1819,
one CA1720, one CA1710, two CA1032 and three CA2100. That library failure prevented
host compilation at that source stage; new argument/ownership cases have not executed.
The exact before-source cut is preserved in ignored artifacts. ADR-044 now accepts
the separate immutable harness/naming stage with valid JSON/configuration/wire
values preserved; source or malformed-input fixtures do not qualify remote cleanup,
numeric coverage or performance. The later numeric rules and both analyzer project
graphs compile in `strict-analyzer-numeric-tests-build.log`. The historical full
numeric cut is `strict-solution-numeric-inventory.log` and its JSON inventory.
Subsequent Core and Client joins passed in `strict-core-numeric-guard-join.log` and
`strict-client-numeric-predicate-reviewed.log`, respectively. The later solution
cut `strict-solution-private-storage-join.log` contains54errors/0warnings and is
not exhaustive. The reviewed joins are0errors/0warnings in
`strict-storage-private-owner-final-review.log`,
`strict-comparison-numeric-readiness-final.log`,
`strict-security-redaction-guard-join.log`, `strict-cli-feature-owner-final.log`
and `strict-query-feature-owner-final.log`. The reviewed Host cleanup and SiteTests
joins are also0errors/0warnings in `strict-comparison-host-cleanup-final.log` and
`strict-site-process-quality-final.log`. Site JSON metadata owns copied options,
and all seven actual generated DTO creators are parameterless with unchanged
initializers. The real Node/options/default/null and static-listener regressions
are source-authored only. The older UnitTests dependency graph emitted225errors/
0warnings in `strict-unit-host-site-prerequisites.log`. The latest recorded joined
build is114errors/0warnings in `strict-unit-query-storage-embedded-corrected.log`,
with its matching deduplicated inventory; no finding belongs to the reviewed
query/storage/prepared/embedded scopes. The earlier whole-solution build,
`strict-solution-host-site-join.log`, stopped at19
errors/0warnings. Its verified inventory is `strict-solution-host-site-inventory.json`;
source hashes are explicitly post-build shared-checkout snapshots, not an exact
delivered SHA. That historical prerequisite cut predates the clean enabled
embedded-host build and latest114-finding UnitTests cut. New source work requires
a fresh integrated build and retains all old raw evidence.
These test migration/style findings need accepted bounded joins, with fake
comparison cases replaced by real caller proof. Full graph, source-owned rule execution, complete formatter
and exact-SHA CI remain pending. Coverage research is complete; compatible TUnit
already resolves extension18.11.2 with MTP2.4.1, but explicit collection, container
export and authentic numeric baseline remain pending.
Reports are build artifacts, not committed source or a fabricated green baseline.

Final reviewed source packets preserve the UTF132-method scope, CDE77original+4new,
all43 B methods/10arguments and original allocation windows. B3's throwing wrapper
retains every original error and final actual-worker awaits. Two exact method
groups and the five-file literal/token-identical whitespace packet were reviewed;
the two resulting long methods received a separate preserving coverage-helper
extraction (live type190/method47, adapter type58/method46 by source audit).
CQ018's six existing ordered-content assertions preserve all7methods, exact bytes,
WAL/positions/reopen and real ManagedCode transfer. Every packet has full hashes/
diff/source maps and strongest plus lead review; none is runtime/test qualification.
The earlier lifetime build stopped at ArtifactTransfer CS0029 before UnitTests
compilation; the newer source-quality build clears that alias defect but stops
at the concurrently edited protected Core prerequisite. Neither one-error cut is
an exhaustive current solution inventory. The previous broad formatter's112
findings included protected BlobStorage; that raw static cut is preserved and is
not this stage's write scope or a compiler result. Accepted source joins and their
open combined gates are in [quality plan](../Features/CodeQuality.md).

## Canonical CI baseline

Dispatched after the task plan: [36930711012](https://github.com/managedcode/KeyLoad/actions/runs/36930711012),
source SHA `9c570f8c33a7a9667507a8e1c0ca68860de3be45`, completed successfully.
Every required baseline job succeeded:

- [verify Ubuntu](https://github.com/managedcode/KeyLoad/actions/runs/36930711012/job/110598991921).
- [verify Windows](https://github.com/managedcode/KeyLoad/actions/runs/36930711012/job/110598991953).
- [verify macOS](https://github.com/managedcode/KeyLoad/actions/runs/36930711012/job/110598992063).
- [comparison smoke and profiles](https://github.com/managedcode/KeyLoad/actions/runs/36930711012/job/110598991552).

Downloaded run artifacts to `artifacts/code-quality/baseline-ci/`; the comparison
suite includes smoke, json-1k-c8 and json-16k-c4 JSON/Markdown/CSV results.
Run metadata is retained in `artifacts/code-quality/baseline-run.json`.
This run covers existing committed main, not the dirty checkout or new analyzers.

The failed build/formatter rows above are source-specific historical snapshots.
Commit `3559225a5f918160e46e32c9a812c3f71790e382` is pushed to protected `main`.
Exact-SHA run [36988949282](https://github.com/managedcode/KeyLoad/actions/runs/36988949282)
failed: the analyzer suite passed 84/88 tests, comparison passed 2/4, and Docker/Aspire
RF3 passed 3/23. Governance failed on Ubuntu, macOS and Windows; UnitTests and
child-process recovery did not run because the verification jobs stopped at
governance. The known span/cohesion, resource naming/image-scope, RF3 invalid-read,
and shared-cluster cleanup repairs are being joined. This run does not qualify the
candidate; a new full exact-SHA workflow is required. ADR-033 remains Accepted and
numeric coverage is still unconfigured.
