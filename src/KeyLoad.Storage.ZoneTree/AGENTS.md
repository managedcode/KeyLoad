# KeyLoad.Storage.ZoneTree

## Purpose and entry points
- Owns the ZoneTree-backed storage adapter and checkpoint operations.
- Main public entry point: `ZoneTreeStore.cs`; the replaced `Checkpoints.cs` behavior is owned by `Features/StorageRecovery/ZoneTreeCheckpointManager.cs` and the checkpoint codec/generation components.

## Ownership and boundaries
- Storage feature behavior belongs under `Features/<SliceName>/` and its matching feature spec; keep provider-specific code behind storage abstractions.
- Physical ownership is node-local: `PartitionHost` owns storage, journals, file locks and apply gate. Atomic partition identity is distinct from physical placement; Orleans grains route commands and activation migration must not migrate storage ownership.
- Current root-level adapter files are migration debt. Do not claim power-loss durability from process-kill evidence or alter storage formats without an ADR and recovery contract.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Storage and checkpoint behavior is exercised by `dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore --configuration Release` and RecoveryTests through `.github/workflows/ci.yml`; tests run only in GitHub Actions.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve atomicity, ordering, crash-recovery and disposal invariants. Do not add fake storage proofs, local qualification runs or unlocked dependency versions.

## Read-first and canonical slice ownership
- Disposable point-cache infrastructure belongs to `Features/ResourceExecution/` under ADR-058. StorageRecovery still owns the real gate, Apply, snapshots and recovery joins; the cache cannot replace native records/WAL, scoped reads or authorization. Explicit embedded opt-in uses an externally shared pool; RF3 admission remains cold until the authenticated Orleans control contract is delivered and qualified. Index, fills and retired pinned bytes remain charged for their actual owned lifetime.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `StorageRecovery`; target feature path: `Features/StorageRecovery/`, matching `docs/Features/StorageRecovery.md`.
- `ZoneTreeStore.cs` is the public provider entry point; private behavior, including the replaced `Checkpoints.cs`, MUST remain under the named slice. Local backup/restore is owned by the matching `Features/BackupRestore/` slice.
- ADR-046 accepts source-only replacement of private facade partial behavior with cohesive `Features/StorageRecovery/` runtime/journal/read/checkpoint owners and `Features/BackupRestore/` local backup helpers. The same public facade remains the physical owner; preserve formats, one gate/lock/tree/WAL, distinct cleanup orders and all required real qualification. Exact scope and migration join are in the `docs/Features/StorageRecovery.md` acceptance/execution contract and ADR-046; no new exception or ownership transfer is authorized.
