# KeyLoad.ComparisonHost

## Purpose and entry points
- Owns the sole comparative CLI, composed by Program.cs and feature-owned helpers under Features/BenchmarkComparisons/.
- Read root AGENTS.md, docs/Architecture.md, BenchmarkComparisons feature, ADR-033/034/043 and comparison-host acceptance/plan before implementation.

## Ownership and boundaries
- KeyLoad.Comparisons retains the shared public library; this host owns only environment/CLI composition and lifecycle. Preserve public CLR, report/schema, configuration, current target registration and workload contracts.
- Aspire owns resource lifecycle; the comparisons resource keeps its environment, waits and image pins. Never replace the required KeyLoad RF3 topology.
- Keep Program composition-only and feature behavior under the canonical slice. No new package, duplicate entry, compatibility shim, suppression or hardcoded machine keys.

## Commands and evidence
- Development prerequisite: dotnet build benchmarks/KeyLoad.ComparisonHost/KeyLoad.ComparisonHost.csproj --no-restore --configuration Release with real dependencies and central analysis.
- Formatter: dotnet format KeyLoad.slnx --verify-no-changes --no-restore. Static governance: node scripts/Features/RepositoryGovernance/verify.mjs.
- TUnit/MTP startup, recovery, comparison and Docker/Aspire qualification execute only in GitHub Actions. No local tests or load benchmarks. Preserve exact SHA/run/artifacts.

## Skills and protected risks
- Applicable skills: none installed for this host; no skill/tool installation or global configuration changes.
- Track each client/target ownership transfer and partial construction cleanup; detach console handlers before lifetime disposal. Keep credentials external and error output safe.
- Preserve every unrelated shared-checkout change. Workers own only this new project; the lead alone changes library, solution, AppHost, inventory, docs and tests references.

## Vertical-slice responsibility folders
- Keep feature-owned implementation inside its canonical `Features/<SliceName>/` and organize it in populated, feature-local responsibility folders (such as `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Transport/`, `Hosting/`, `Serialization/`, or `Validation/`). Do not leave a flat dump of unrelated responsibilities at the slice root; keep only genuinely shared building blocks and executable/composition entry points outside feature slices. Preserve namespaces and runtime contracts during physical moves.
