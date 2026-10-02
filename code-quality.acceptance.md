# CodeQuality acceptance

Goal: developers can author KeyLoad Roslyn rules in C#, see diagnostics in the IDE
and build, and review compiler reports. Chosen direction: [brainstorm](code-quality.brainstorm.md).
Durable owner: [CodeQuality](docs/Features/CodeQuality.md), ADR-033.

## Scope, actors and boundaries

In scope: exact Prostir EditorConfig; explicit .NET/style/live/build analyzer
enablement and warnings-as-errors for all projects; eight portable custom rules;
real compiler regression tests; per-project SARIF; CI build/format/analyzer-test
gates and report upload. Developers edit sources; GitHub Actions qualifies them.
Out of scope: public database contracts/topology, dependency upgrades, global
configuration/skills, unrelated source refactoring and the four Prostir-specific
analyzers (product commands, Studio identity, Cosmos queries, storage-provider
choice). These exclusions are applicability decisions, not severity suppressions.
No runtime data/API/UI or credentials change. No new NuGet dependencies are needed
for the analyzer; tests use centrally pinned TUnit and real existing Orleans SDK.

Assumptions: .NET 10.0.401 hosts the Roslyn analyzer as in Prostir. The source
EditorConfig's existing disabled rules remain exactly as supplied. All SDK rules
available through latest-all run, subject to this owner-selected configuration.
Old source may fail stricter rules; failures stay visible, not baselined away.
The dirty checkout cannot be represented as a qualified remote SHA.

## Measurable criteria

- AC-CQ-001 / REQ-CQ-001: `.editorconfig` bytes equal Prostir's source at import;
  every solution project evaluates EnableNETAnalyzers, EnforceCodeStyleInBuild,
  RunAnalyzersDuringBuild/LiveAnalysis, TreatWarningsAsErrors and
  CodeAnalysisTreatWarningsAsErrors and GenerateDocumentationFile to true and
  AnalysisLevel to latest-all. Documentation output enables the imported IDE0005
  build diagnostic; do not copy Prostir's NoWarn suppression list.
  Fail: missing/overridden setting or additional suppressions.
- AC-CQ-002 / REQ-CQ-002: source-owned KeyLoad.Analyzers attaches as an Analyzer
  ProjectReference to every ordinary project. Analyzer and its tests are excluded
  from recursive attachment but retain SDK analysis. Preserve product behavior;
  adapt KeyLoad assembly names, prefixes and MapKeyLoadApi boundary. Fail: sibling
  dependency, cycles, runtime reference, rules left targeting Prostir.
- AC-CQ-003 / REQ-CQ-003: compiler diagnostics are stored as SARIF 2.1 per project,
  configuration and framework; CI uploads them even on build failure. Fail: shared
  filename collision, missing location/rule ID or fabricated success report.
- AC-CQ-004 / REQ-CQ-002: TUnit uses real Roslyn compilations and real Orleans
  metadata; each rule has invalid/valid examples, ID/severity/location assertions,
  generated-source/external-boundary cases where relevant, and no compiler errors
  in semantic fixtures. No mocks/stubs or local test executions.
- AC-CQ-005 / REQ-CQ-004: CI retains all existing suites and adds analyzer tests
  plus formatter verification. SDK/custom rules cannot be disabled project-locally.
  Fail: weakened severity, skipped suite, NoWarn additions or continue-on-error.
- AC-CQ-006 / REQ-CQ-004: new project local policies, feature/ADR/architecture and
  README/status describe real commands, ownership, exclusions and observed gates.
  Missing numeric coverage/complexity qualification remains pending. Existing
  unrelated edits, policies, lock-file deletions and runtime changes survive.

## Criterion-to-verification matrix and methodology

| AC | Positive / negative / error flow | Automated owner and assertions | Command / evidence |
|---|---|---|---|
| 001 | All project settings true; disabled override fails | Real MSBuild evaluation and policy target; EditorConfig SHA comparison | dotnet msbuild project -getProperty; cmp source/import; CI build |
| 002 | Ordinary consumers load DLL; infrastructure has no recursive analyzer ref | Real build project graph, inspect Analyzer item, diagnostic contract tests | dotnet build KeyLoad.slnx --no-restore --configuration Release; CI |
| 003 | Rule and line reported; failing builds still retain independent SARIF paths | Real Csc ErrorLog JSON; CI always-upload | artifacts/code-quality; code-quality-OS artifact |
| 004 | Each rule accepts valid and rejects invalid code; irrelevant external/generated code handled | tests/KeyLoad.Analyzers.Tests/Features/CodeQuality | CI dotnet test --project tests/KeyLoad.Analyzers.Tests --no-build --no-restore --configuration Release |
| 005 | Formatter/analyzer failure is a failed gate; existing suites retained | CI execution and workflow diff review | gh workflow run ci.yml --repo managedcode/KeyLoad --ref main |
| 006 | Accurate current gates and preserved checkout | Static governance plus manual diff review | node scripts/Features/RepositoryGovernance/verify.mjs; git diff --check |

Manual exceptions: import byte equality and central MSBuild evaluation are static
checks, not TUnit behavior tests. Policy preservation/document honesty require
lead review. Existing unrelated quality debt is reported with compiler evidence;
this task cannot claim qualification until CI for the delivered change passes.
Coverage remains an unconfigured mandatory gate; no numeric result is invented.
CI-only execution also means no local analyzer tests, even if the standalone
analyzer compiles. Reporting failure is tested through actual diagnostic output.

Migration: self-contained source import, no persisted format changes. Rollback is
a normal reviewed revert of this scoped policy/import. No command may disable
analyzers to hide failures or change the owner's requested EditorConfig.
## Remaining preserving prerequisites

AC-CQ-007 / REQ-CQ-005: diagnosed benchmark public XML describes the actual driver,
ownership and measurement semantics without changing CLR bodies or visibility.
Named machine keys retain their exact values. Private HTTP overloads retain the
same relative/absolute URI interpretation; typed cleanup catches retain rethrow
and disposal; the PostgreSQL null check uses the real asynchronous driver API and
caller cancellation. Required public null inputs reject with ArgumentNullException
before corpus construction or target work. Real wall time and sampler measurement
interval/counter definitions remain unchanged. Fail: API/array/enum/SQL/report or
topology migration in this stage, hidden diagnostics, fake verification or a claim
that unexecuted regressions passed.

Criterion-to-test matrix: the lead authors real configuration/corpus/statistics/
runner null, empty-target and valid configuration TUnit regressions before guards
in UnitTests/Features/BenchmarkComparisons/ComparisonArgumentTests.cs. They execute
only in canonical exact-SHA GitHub Actions after the solution build. Existing real
engine/comparison suites qualify URI, driver, lifecycle and clock preservation;
source-compatible private overload/style changes add no artificial mirrored tests.
XML-only additions receive an explicit static evidence exception: lead diff/token
review, documentation semantics review and actual strict dependency build. No
numeric coverage or runtime result follows from these static checks. Public schema
and SQL design remain outside this preserving stage and require their own accepted
implementation contract before writes.
### Adapter ownership and argument extension of AC-CQ-007

Numeric extension: REQ-CQ-006 / AC-CQ-008/009 are defined in the detailed
[quality-gates.acceptance.md](quality-gates.acceptance.md), with their own task graph
and accepted ADR-033 implementation contract. Numeric source fixtures precede four
new enabled error rules; compatible functional coverage/container export and matched
baseline remain mandatory pending work. Original rule semantics and copied
EditorConfig bytes remain unchanged.

The missing dataset rejects before HTTP/driver work in each diagnosed target.
Never-initialized HTTP targets dispose clients without a remote cleanup request;
Neo4j/Qdrant retain cleanup once resource creation is attempted, even if creation
or later setup fails. KeyLoad disposes the primary client when supplied peers omit
it, with distinct client disposal. Real ordinary HttpClient/native target TUnit
cases in NEW HttpTargetArgumentTests.cs and NativeTargetArgumentTests.cs map the
negative/no-initialization and client-ownership flows; no handlers/doubles/local
execution are allowed. Existing Docker/Aspire real engine scenarios and planned
partial-setup fault evidence qualify successful/uncertain remote cleanup. An authored
negative test alone is not full lifecycle, coverage or runtime qualification.
