# KeyLoad.ComparisonHost

## Purpose and entry points
- Owns the native TUnit comparison executable and feature-owned helpers under BenchmarkComparisons/.
- Read root AGENTS.md, docs/Architecture.md, BenchmarkComparisons feature, ADR-033/034/043 and comparison-host acceptance/plan before implementation.

## Ownership and boundaries
- KeyLoad.Comparisons retains the shared public library; this host owns environment composition and lifecycle. ADR-142 replaces superseded benchmark selectors and report schemas under the owner's explicit retirement instruction; preserve genuine native target operations and current session provenance.
- Aspire owns resource lifecycle; the comparisons resource keeps its environment, waits and image pins. Never replace the required KeyLoad RF3 topology.
- Keep Program composition-only and feature behavior under the canonical slice. No new package, duplicate entry, compatibility shim, suppression or hardcoded machine keys.

## Commands and evidence
- GitHub prerequisite: dotnet build benchmarks/KeyLoad.ComparisonHost/KeyLoad.ComparisonHost.csproj --no-restore --configuration Release with real dependencies and central analysis. All benchmark-stage verification executes on Linux GitHub Actions under the root owner correction2026-10-09.
- Formatter: dotnet format KeyLoad.slnx --verify-no-changes --no-restore. Static governance: node scripts/Features/RepositoryGovernance/verify.mjs.
- TUnit/MTP startup, recovery, comparison and Docker/Aspire qualification execute only in GitHub Actions. No local tests or load benchmarks. Preserve exact SHA/run/artifacts.

## Skills and protected risks
- Applicable skills: none installed for this host; no skill/tool installation or global configuration changes.
- Track each client/target ownership transfer and partial construction cleanup; detach console handlers before lifetime disposal. Keep credentials external and error output safe.
- Preserve every unrelated shared-checkout change. Workers own only this new project; the lead alone changes library, solution, AppHost, inventory, docs and tests references.

## Vertical-slice responsibility folders
- Keep feature-owned implementation inside its canonical `<SliceName>/` and organize it in populated, feature-local responsibility folders (such as `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Transport/`, `Hosting/`, `Serialization/`, or `Validation/`). Do not leave a flat dump of unrelated responsibilities at the slice root; keep only genuinely shared building blocks and executable/composition entry points outside feature slices. Preserve namespaces and runtime contracts during physical moves.

## Native benchmark test execution, owner correction 2026-10-07
- The comparisons container MUST be a native TUnit executable. Its test invokes the existing real C# client workload and asserts the original report outcome after Aspire prerequisite readiness. Preserve separate database jobs, measured client operations, timings, resource observations, cancellation and report provenance. This supersedes the former CLI entry; no standalone non-test workload entry remains.
- Owner correction2026-10-17 requires separate native workload tests sharing one live database per database/node-count job, with measured million-record loading and no warmup, reset or reseed. The current session fixture belongs under `BenchmarkComparisons/Session/`; retain each test's original outcome and the shared owned shutdown.
