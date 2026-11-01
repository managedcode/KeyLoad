# KeyLoad.Storage.ZoneTree

## Purpose, entry points and ownership

- Owns the ZoneTree-backed storage adapter and native live-backup wrappers.
- Main public entry point: `ZoneTreeStore.cs`. `StorageEngine` owns the native transactional and plain ZoneTree lifetimes; `Restore` owns native live backup, restore and file-storage copy. Use native snapshots, serializable conflict detection, async WAL sync and `CreateMaintainer`; add no KeyLoad write serialization gate, logical point cache or maintenance scheduler.
- Storage feature behavior belongs under `<SliceName>/` and its matching feature spec; keep provider-specific code behind storage abstractions.
- Physical ownership is node-local: every silo has its own ZoneTree owner (`PartitionHost`/Grain Service) that owns its native storage, WAL and exclusive file ownership. Orleans grains route commands, and activation migration must not migrate storage ownership; files never move with activations. See [ADR-136](../../docs/ADR/ADR-136-orleans-partition-ownership.md).
- Apply the installed Orleans skill when changing the boundary between silo-local storage and Orleans execution. storage recovery remains owned by this adapter.
- Preserve atomicity, ordering, crash-recovery and disposal invariants. Do not add fake storage proofs, present local development evidence as delivered-source qualification, or use unlocked dependency versions.
- Follow the root dependency-ownership and delivery policy. ZoneTree findings are reported privately in a local root `zonetree-private-report-<topic>.md`; never publish them, attach them to CI, or contact maintainers on the owner's behalf.

## Storage access modes

Follow [ADR-136](../../docs/ADR/ADR-136-orleans-partition-ownership.md).
Isolated volumes have silo-local file owners reached through Orleans. A shared
volume requires one open owner for each mutable physical partition, lazy opening
and joined close before another silo reopens the same files; no data copies are
created by a shared mount. Never open the same mutable ZoneTree eagerly on every
silo. Cross-partition queries address logical partitions once and aggregate
bounded partial results without counting replicas twice. These lifetime and
query paths remain open implementation and qualification gates; a configuration
flag alone does not implement them. StatelessWorker activations are automatically
managed by Orleans and are not individually addressable.

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
