# KeyLoad.ComparisonTests

## Benchmark contract ownership, owner correction 2026-10-06

- Own all benchmark/comparison contract tests previously held in KeyLoad.UnitTests, including corpus, progress, process-entry and counter checks. Keep them under Features/BenchmarkComparisons/UnitContracts with responsibility folders and preserve their existing namespaces and assertions during this structural move. Execute them through the Aspire comparison suite exclusively in Benchmarks; Build and Tests must not execute this project.

## Purpose and entry points
- Owns TUnit comparison-harness checks and the CI comparison workload entry point.
- Main suite: `Features/BenchmarkComparisons/Cases/ComparisonTests.cs`; project: `KeyLoad.ComparisonTests.csproj`.

## Ownership and boundaries
- Tests and comparison artifacts belong to the `BenchmarkComparisons` slice and matching `docs/Features/BenchmarkComparisons.md`.
- This project validates the comparative harness and emits controlled CI evidence; it does not establish production readiness or own product runtime behavior.
- Keep positive, negative and error assertions meaningful. Never weaken checks or replace the real RF3 KeyLoad target with a fake/in-memory substitute.

## Commands and evidence
- Canonical invoking GitHub Actions commands: `dotnet build tests/KeyLoad.ComparisonTests --no-restore --configuration Release` and `dotnet test --project tests/KeyLoad.ComparisonTests --no-build --no-restore --configuration Release` in `.github/workflows/ci.yml`.
- Owner clarification 2026-10-03 moves those comparative commands to the separate `.github/workflows/benchmarks.yml` (`Benchmarks`) pipeline, including native TimeSeries image checks. The historical ci.yml evidence remains authentic; new isolated producer guards must bind to the new exact name/path (ADR-062).
- Workflow runs comparison cases with controlled environment settings and uploads `comparison-suite`; do not run these tests or benchmarks locally. Preserve run URL, SHA and artifact links for any published result.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- CI comparison data is distinct from durability, endurance and production qualification. Existing runner/framework migration gaps remain governed by root policy; TUnit is the target.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned test slice: `BenchmarkComparisons`; target path: `Features/BenchmarkComparisons/`, matching `docs/Features/BenchmarkComparisons.md`.
- `Features/BenchmarkComparisons/Cases/ComparisonTests.cs` is the current entry point; keep test scenarios under the same named slice.
## Feature slice responsibility folders
- Place every feature-owned source file under `Features/<SliceName>/<Role>/`, using populated feature-local roles such as `Cases/`, `Fixtures/`, `Assertions/`, `Models/`, `Processes/`, `Contracts/`, `Serialization/` or `Helpers/` according to the file's actual responsibility. Preserve an existing nested scenario/domain folder and add the role beneath it. Create only roles that own files.
- Keep genuinely shared test infrastructure and project composition entry points at their existing shared ownership paths; do not duplicate them into a feature.
- Structural moves preserve every source byte, namespace, type/serializer identity and test assertion. Do not change behavior, test logic or path references as part of a layout-only move; report source-path-sensitive joins to the solution integrator.

## Native TUnit entry, owner correction 2026-10-07
- ADR-117 supersedes the earlier outer AppHost caller requirements: CI starts TUnit directly after build with Detailed output. Test fixtures own Aspire infrastructure startup, readiness, client operations and cleanup. scripts/Features/TestInfrastructure/run-tests.mjs only selects native test arguments/environment; it cannot execute database workloads. RF3 coverage preparation belongs to the TUnit session lifecycle. Preserve every original qualification/artifact gate and separate Benchmarks ownership.


## Current native comparison topology, owner direction 2026-10-09

- ADR-122 supersedes every active two-node comparison inventory: current benchmark planning, admission, original-file provenance and site projection accept only native one-node or three-node cells. Reject two-node current inputs before acquisition; RF3 majority remains two acknowledgements. Preserve immutable historical original bytes/hashes without a legacy reader or current qualification fallback.
- The existing control/scale/vector family now has 924 workers and 1,716 original inputs (1,656 suite files and 60 provider files), with the 84-path executed source closure. This current topology contract supersedes earlier active 1,386-worker/2,530-input clauses; new document-family evidence must receive its own exact inventory before publication. All source, authorization, native operation, complete cohort, provenance and coverage gates remain mandatory.
