# KeyLoad.Orleans

## Purpose and entry points
- Owns Orleans grain routing and Orleans-backed membership integration.
- Main types: `Features/ClusterRouting/Grains/RequestGrain.cs`, `Grains/CommandPartitionGrain.cs`, `Grains/DatabaseReadGrain.cs` and `Topology/ReplicaMembershipTable.cs`.

## Ownership and boundaries
- Owner correction 2026-10-04: every feature MUST group its actual responsibilities into feature-local role folders; do not place mixed grains, commands, queries, contracts, models, streaming, identity, serialization or topology files directly in the slice root. `ClusterRouting` uses `Grains/`, `Commands/`, `Queries/`, `Contracts/`, `Models/`, `Streaming/`, `Identity/`, `Serialization/`, `Diagnostics/` and `Topology/`. `ClusterReplication` separates grain services, discovery, transport, authentication, replay, contracts and models. `ResourceExecution` separates models, contracts, serialization, authentication and validation. Small capability slices use `Queries/`; do not create empty role folders.
- Structural migrations MUST preserve file contents, existing namespaces, generated serializer aliases/Ids, interfaces and behavior. Update live path references and architecture maps together; never recreate the old flat path while another task edits the moved file.
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
- `Grains/RequestGrain.cs`, `Grains/CommandPartitionGrain.cs`, `Grains/DatabaseReadGrain.cs` and `Topology/ReplicaMembershipTable.cs` are current entry points; grain routing belongs in the named slice.

## ResourceExecution ownership
- `Features/ResourceExecution/` owns the accepted ADR-058 internal cache-control metadata and bounded wire/authentication/correlation prerequisites, with matching ResourceExecution feature docs and tests.
- Root is the sole integration owner of shared generated types, stable aliases/Ids, primitives, configuration and later lifecycle/composition. Test workers own only their explicitly assigned new test files.
- The R82 primitive stage has no production caller, eligibility side effect, receiver, timers, DI/cache enablement or persisted/public/replica format migration. Later stages require a separately frozen contract and genuine RF3 evidence.
- Read [the local slice policy](Features/ResourceExecution/AGENTS.md) and [CacheControlV1](../../docs/Features/ResourceExecution/CacheControlV1.md) before implementation.
- The earlier bootstrap statement about no installed skills does not negate the explicitly owner-authorized Orleans skill listed above; keep the prohibition on any other unapproved installation.
