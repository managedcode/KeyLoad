# KeyLoad.Orleans

## Purpose and entry points
- Owns Orleans grain routing and Orleans-backed membership integration.
- Main types: `CommandRouterGrain.cs` (`CommandRouterGrain`) and `ConsensusMembershipTable.cs`.

## Ownership and boundaries
- Orleans is the database and cluster foundation. Each request uses a separate Orleans grain that invokes the required database grains; enable distributed grain directory and activation migration/repartitioning.
- Grain activations route commands; they do not own node-local storage. A node-local `PartitionHost` owns storage, journals, file locks and the apply gate, including when activation routing migrates. Keep atomic partition identity separate from physical placement.
- New feature-owned grain behavior belongs under `Features/<SliceName>/` and matching feature docs. Existing root-level files are migration debt. Do not use or expand DotNext.AspNetCore.Cluster or DotNext.Net.Cluster.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Orleans flows are exercised by `dotnet test --project tests/KeyLoad.IntegrationTests --no-build --no-restore --configuration Release` and recovery suite in `.github/workflows/ci.yml`; execute only in GitHub Actions.

## Skills and protected risks
- Owner-authorized applicable skill: Orleans 3.1.1 at `/Users/ksemenenko/.codex/skills/orleans/SKILL.md`; read it and relevant lifecycle/hosting references before implementing. The prior bootstrap prohibition remains for other unapproved installations.
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Activation migration must not move storage ownership. Preserve real cluster topology, membership correctness and cancellation behavior; never claim local or single-node tests qualify RF3.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `ClusterRouting`; target feature path: `Features/ClusterRouting/`, matching `docs/Features/ClusterRouting.md`.
- `CommandRouterGrain.cs` and `ConsensusMembershipTable.cs` are current entry points; grain routing belongs in the named slice.
