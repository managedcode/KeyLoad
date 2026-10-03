# KeyLoad.ComparisonTests

## Purpose and entry points
- Owns TUnit comparison-harness checks and the CI comparison workload entry point.
- Main suite: `ComparisonTests.cs`; project: `KeyLoad.ComparisonTests.csproj`.

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
- `ComparisonTests.cs` is the current entry point; keep test scenarios under the same named slice.
