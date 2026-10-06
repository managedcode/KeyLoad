# KeyLoad.AppHost

## Purpose and entry points
- Owns Aspire application composition for KeyLoad services and infrastructure resources.
- Entry point: `Program.cs`; benchmark resource composition: `Features/BenchmarkComparisons/Resources/BenchmarkResources.cs`.

## Ownership and boundaries
- Keep host wiring and genuinely shared infrastructure composition here. Feature-specific deployment behavior belongs under `Features/<SliceName>/` and the matching `docs/Features/<SliceName>.md`.
- Aspire owns resource orchestration, not database storage state or atomic partition identity. Each node-local `PartitionHost` owns storage, journals, file locks and the apply gate; Orleans grains route commands and physical placement remains separate from atomic partition identity.
- ClusterResources composes actual Docker RF3 resources. Do not replace the required Docker/Aspire RF3 topology with an in-memory or single-node demonstration. The accepted ADR-082 protocol-cohort test seam preserves fixed three-voter membership and node-local storage ownership.

## Commands and evidence
- The canonical Aspire-owned test entry is defined by root AGENTS.md and ADR-074. AppHost defaults to Release because the pinned Aspire CLI evaluates RunCommand without forwarding outer dotnet-run configuration; verify the actual launched path and native TUnit outcomes. Do not bypass CLI/DCP or count stale Debug output as current qualification.
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Owner correction2026-10-03 requires every test caller, including integration/RF3, to use `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=<suite>`. Native dotnet test is only the owned child process; preserve all suites and original artifacts. Delivered-source qualification remains actual Linux GitHub Actions evidence.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Owner correction2026-10-03 explicitly permits local development through the same Aspire-owned entry. Preserve secret boundaries, resource health dependencies, scoped fault injection, complete resource shutdown and the required Orleans RF3 topology; local results do not qualify delivered-source Linux or public performance gates.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned composition slices: `ClusterReplication`, `ClusterRouting`, `TestInfrastructure` and `BenchmarkComparisons`; target paths use the same `Features/<SliceName>/` convention. ClusterRouting's protocol image overrides follow NativeCqrsRequestV2 and ADR-082; they are allowed only in an explicitly selected ephemeral fixed-three-voter child test topology, never benchmark or ordinary production composition.
- Keep `Program.cs` as the shared Aspire composition entry point; put feature-owned resource definitions under their named slice.
- `Features/CodeQuality/` owns the test-only native functional coverage configuration and Aspire preparation/resource dependencies under ADR-033 and CodeQuality AC-CQ-039..042. Use the same centrally pinned Microsoft engine as MTP, downloaded as a package; do not install a global tool. Preserve exact contributor/source/DLL/PDB/image identity, original process settlement and all full mandatory suites. Runtime coverage still comes from the original owned server processes, not the AppHost assembly.

## Vertical-slice responsibility folders
- Keep feature-owned implementation inside its canonical `Features/<SliceName>/` and organize it in populated, feature-local responsibility folders (such as `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Transport/`, `Hosting/`, `Serialization/`, or `Validation/`). Do not leave a flat dump of unrelated responsibilities at the slice root; keep only genuinely shared building blocks and executable/composition entry points outside feature slices. Preserve namespaces and runtime contracts during physical moves.
