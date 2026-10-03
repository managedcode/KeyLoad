# KeyLoad.Benchmarks

## Purpose and entry points
- Owns the BenchmarkDotNet executable for KeyLoad-focused microbenchmarks.
- Executable entry point: `Program.cs`; project definition: `KeyLoad.Benchmarks.csproj`.

## Ownership and boundaries
- New benchmark behavior belongs under `Features/<SliceName>/`; use the same canonical slice name as the corresponding product feature and `docs/Features/<SliceName>.md`.
- This project measures code paths; it does not own database behavior, comparison corpus/report contracts, cluster orchestration, or qualification claims.
- The current root-level `Program.cs` is an executable entry point and existing layout migration debt. Do not expand layer-first or root-level feature ownership.

## Commands and evidence
- Solution build in GitHub Actions: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- There is no benchmark test project command for this project. Never run load benchmarks locally; performance evidence must come from the designated GitHub Actions workflow and its artifacts.
- Any helper or suite that invokes these benchmarks must document and use its existing GitHub Actions invocation.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Do not turn microbenchmark output into RF3, durability, endurance, or production-readiness evidence. Preserve centrally pinned packages and the no-lock-file policy.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `BenchmarkComparisons`; target feature path: `Features/BenchmarkComparisons/`, matching `docs/Features/BenchmarkComparisons.md`.
- `Program.cs` remains the executable entry point; benchmark behavior and scenarios belong in that named slice.

## ADR-047 generated consumer boundary
- Accepted ADR-047 moves the complete public fixture/scenario to the solution-owned KeyLoad.BenchmarkScenarios library; the original executable and command-line defaults remain here through a typed runner selecting that fixture assembly.
- Existing fixture ownership above records the historical baseline during this coherent migration. Do not keep duplicate or compatibility fixture bodies here after the accepted source join.
- The library fixture must be public/unsealed for BenchmarkDotNet's actual generated child; this executable contains internal host types and never disables CA1515 or another quality rule.
- Real generated-process Dry qualification is invoked by TUnit only in GitHub Actions, with exact SHA and raw reports. It is not a performance or RF3 measurement claim.

## Owner-authorized local optimization, 2026-10-03
- The explicit root owner correction supersedes the historical local execution prohibitions above for development verification. Run genuine local BenchmarkDotNet for code optimization, retaining exact source, settings, machine and original outputs. Internal raw/code measurements must not appear in benchmarks.yml. Website database figures require authenticated original GitHub full-database comparisons and matched verified hardware, resources, topology, durability and workloads; local results remain development evidence.
