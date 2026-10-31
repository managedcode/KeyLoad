# KeyLoad.Clustering

## Purpose, entry points and ownership

- Owns the Orleans grains and Grain Services that execute database operations: one grain per connected client admits operations; one request grain per operation calls the silo-local ZoneTree owner on its own or another node through Orleans, gathers the results and returns them.
- Main types: `RequestExecution/Grains/ConnectionGrain.cs`, `RequestExecution/Grains/RequestGrain.cs`, `RequestExecution/Grains/RequestExecutionGrain.cs` and `RequestExecution/Queries/Connections/ConnectionReadExecution.cs`.
- Every feature MUST group its actual responsibilities into feature-local role folders; do not place mixed grains, commands, queries, contracts, models, streaming, identity or serialization files directly in the slice root. `RequestExecution` owns connection/request grains and bounded request streams; `RuntimeJournaling` owns journal storage; `OrleansHosting` owns membership options; `StoragePartitions` owns physical-owner contracts; `BackgroundExecution` owns shared worker contracts. `StorageReplication` separates grain services, execution, contracts and models. Small capability slices use `Queries/`; do not create empty role folders.
- Structural moves MUST preserve behavior, generated serializer aliases and stable field IDs. Keep namespaces, interfaces, live path references and architecture maps coherent; never recreate the old flat path while another task edits the moved file.
- Microsoft Orleans does all clustering (membership, discovery, failure detection, directory, placement, activation migration, communication); copying data to replicas happens only through ordinary Orleans grain/Grain Service calls, and `KeyLoad:ReplicaCount` is the number of copies. Public API → one grain per connected client → grains call the silo-local ZoneTree owner on their own or another node through Orleans, gather and return. Enable the distributed grain directory and activation migration/repartitioning. See [ADR-136](../../docs/ADR/ADR-136-orleans-partition-ownership.md).
- Grain activations route commands; they do not own node-local storage. Every silo has its own ZoneTree owner (node-local `PartitionHost`/Grain Service) that owns its storage, journals, file locks and the apply gate; files never move with activations.
- Apply the installed Orleans skill at `/Users/ksemenenko/.codex/skills/orleans/SKILL.md` and relevant lifecycle/hosting references before implementing Orleans decisions.
- Activation migration must not move storage ownership. Preserve Orleans membership behavior and cancellation; never claim local or single-node tests qualify cluster.

## Storage access modes

Follow [ADR-136](../../docs/ADR/ADR-136-orleans-partition-ownership.md).
`ReplicaCopyOptions.StorageMode` binds `KeyLoad:StorageMode`; it defaults to
`IsolatedVolumes`. `SharedVolume` requires `ReplicaCount=1` and never rewrites a
requested copy count. The silo count remains independently configured.
Isolated volumes have silo-local file owners reached through Orleans. A shared
volume requires one open owner for each mutable physical partition, lazy opening
and joined close before another silo reopens the same files; no data copies are
created by a shared mount. Never open the same mutable ZoneTree eagerly on every
silo. Cross-partition queries address logical partitions once and aggregate
bounded partial results without counting replicas twice. These lifetime and
query paths retain their original scopes, owner pins, cancellation and
budgets. Their complete runtime and delivered-source qualification remains open;
a configuration flag alone is not proof. StatelessWorker activations are
automatically managed by Orleans and are not individually addressable.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
