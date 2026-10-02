# KeyLoad.Comparisons

## Purpose and entry points
- Owns the reusable comparative benchmark harness library and its database targets under ADR-043; the sole executable belongs to KeyLoad.ComparisonHost in this repository.
- Library entry points: Features/BenchmarkComparisons/ComparisonRunner.cs, Features/BenchmarkComparisons/BenchmarkDataset.cs, Features/BenchmarkComparisons/Contracts.cs and ReportWriter.cs; target adapters: Targets/ and the matching feature slice. CLI composition belongs to the host's Program.cs and feature helpers.

## Ownership and boundaries
- Own comparative workload definitions, target adapters, sampling and report serialization. Product semantics remain owned by their product slices; CI callers own qualification and publication decisions.
- Target feature path is `Features/BenchmarkComparisons/`, aligned with `docs/Features/BenchmarkComparisons.md` when that feature spec is established. The current flat files are recorded migration debt; do not expand it.
- Keep the corpus and measurement contract stable and reproducible. Do not substitute an in-memory/single-node KeyLoad target for the required RF3 topology.

## Commands and evidence
- GitHub Actions builds the solution with `dotnet build KeyLoad.slnx --no-restore --configuration Release` and invokes comparison qualification through `tests/KeyLoad.ComparisonTests`.
- Do not run comparisons or load benchmarks locally. Only GitHub Actions artifacts are publishable performance evidence; preserve the exact run, SHA, URLs and artifacts.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Never infer durability or production readiness from benchmark results. Keep all target credentials external and preserve centrally pinned packages and no `packages.lock.json` files.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `BenchmarkComparisons`; target feature path: `Features/BenchmarkComparisons/`, matching `docs/Features/BenchmarkComparisons.md`.
- The sole executable entry point is now KeyLoad.ComparisonHost/Program.cs under ADR-043; all comparison behavior retains the same named slice. Do not restore a duplicate entry or internalize the public library API.
- The runner has moved from the historical flat entry listed above to `Features/BenchmarkComparisons/ComparisonRunner.cs`; measurement and validation helpers belong in the same slice. This source move does not establish GitHub runtime qualification.
