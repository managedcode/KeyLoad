# KeyLoad.AppHost

## Purpose and entry points
- Owns Aspire application composition for KeyLoad services and infrastructure resources.
- Entry point: `Program.cs`; benchmark resource composition: `BenchmarkResources.cs`.

## Ownership and boundaries
- Keep host wiring and genuinely shared infrastructure composition here. Feature-specific deployment behavior belongs under `Features/<SliceName>/` and the matching `docs/Features/<SliceName>.md`.
- Aspire owns resource orchestration, not database storage state or atomic partition identity. Each node-local `PartitionHost` owns storage, journals, file locks and the apply gate; Orleans grains route commands and physical placement remains separate from atomic partition identity.
- Existing RF3 composition launches host processes and is tracked implementation debt. Do not replace the required Docker/Aspire RF3 topology with an in-memory or single-node demonstration.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Integration/RF3 behavior is invoked by `dotnet test --project tests/KeyLoad.IntegrationTests --no-build --no-restore --configuration Release` in `.github/workflows/ci.yml`; execute qualification only in GitHub Actions.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Do not start local AppHost or qualification resources. Preserve secret boundaries, resource health dependencies and the required Orleans RF3 topology.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned composition slices: `ClusterReplication` and `BenchmarkComparisons`; target paths: `Features/ClusterReplication/` and `Features/BenchmarkComparisons/`.
- Keep `Program.cs` as the shared Aspire composition entry point; put feature-owned resource definitions under their named slice.
