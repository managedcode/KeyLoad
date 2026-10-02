# ADR-008: Backup cuts and independent log retention

Status: Accepted; full backup/restore and cluster qualification pending. Product source: [sections 5–6 and 37–45](../design/architecture-v0.3.uk.md); source test status is tracked by [StorageRecovery](../Features/StorageRecovery.md) and peer-owned BackupRestore documentation.

## Context and decision

WAL/transaction journals, replicated command logs, domain event history, queue state, subscription checkpoints, inbox outcomes, and rebuildable projections have different authority and retention. Trimming a recovery log must not silently delete public event history. A backup records an internally consistent catalog epoch and per-atomic-partition cuts for every authoritative resource needed to restore that state. Derived indexes may use a documented rebuild recipe. Restoring an older cut starts with delivery paused and a new cluster incarnation until reviewed and resumed.

## Rationale and consequences

Separate lifecycles prevent log compaction from changing customer-visible history and make restore scope explicit. A backup is not valid if it omits metadata required to interpret data or if it silently replays external effects. Retention pins may consume storage; physical purge across replicas, snapshots and archives needs its own measured and operational contract.

## Related requirements

ClusterReplication `REQ-REP-004`/`AC-REP-004`, StorageRecovery `REQ-STORAGE-004`/`AC-REP-002/004`, EventStreams `REQ-EVENT-005..007`, and Messaging `REQ-MSG-006`. Related files: [ClusterReplication](../Features/ClusterReplication.md), [StorageRecovery](../Features/StorageRecovery.md), and planned [BackupRestore](../Features/BackupRestore.md).

## Implementation contract

1. Freeze authoritative manifest, per-partition cut, retention pins, event-history guarantees, paused restore, and external-effect reconciliation.
2. Add real-store tests for backup consistency, interruption, corruption, retention pins, restore-old-cut token invalidation, and absence of automatic dispatch; qualify replica erase/reinstall separately.
3. Implement in `src/KeyLoad.Core/Features/BackupRestore/`, `src/KeyLoad.Storage.ZoneTree/Features/BackupRestore/`, and replication snapshot ownership under `src/KeyLoad.Replication/Features/ClusterReplication/`.
4. Version manifests and validate every resource before restore. Rollout keeps backups read-compatible; rollback must preserve original archive bytes and stop dispatch if any resource/cut is unknown.
5. GitHub CI runs TUnit, recovery, and RF3 restore drills. Root owns the manifest/cut contract; Messaging/EventStreams join with checkpoint/inbox/retention proof and exact artifacts.

Dependencies: [ADR-003](ADR-003-durability-ack-barrier.md), [ADR-004](ADR-004-committed-read-views.md), [ADR-005](ADR-005-canonical-keyspace-codec.md), [ADR-007](ADR-007-replica-consensus-bootstrap.md), [ADR-011](ADR-011-format-upgrades.md), [ADR-016](ADR-016-atomic-physical-placement.md), and [ADR-030](ADR-030-retention-paused-restore.md). Stop if any authoritative message/history state is omitted or restore would automatically invoke an external side effect.

```mermaid
flowchart LR
    Sources[Canonical data events queue and cursor state] --> Cut[Consistent catalog and partition cuts]
    Cut --> Manifest[Versioned authoritative manifest]
    Manifest --> Validate[Verify resources and checksums]
    Validate --> Restore[Restore paused with new incarnation]
    Restore --> Review[Reconcile external outcomes]
    Review --> Resume[Explicit authorized dispatch resume]
```
