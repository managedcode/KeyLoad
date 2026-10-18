# ADR-008: Backup cuts and independent log retention

Status: Accepted; full backup/restore and cluster qualification pending. Product source: [sections 5–6 and 37–45](../design/product-specification.md); source test status is tracked by [StorageRecovery](../Features/StorageRecovery.md) and [BackupRestore](../Features/BackupRestore.md).

## Context and decision

WAL/transaction journals, domain event history, queue state, subscription checkpoints, inbox outcomes, and rebuildable projections have different authority and retention. Trimming a recovery log must not silently delete public event history. A backup of one silo-local store records an internally consistent manifest and the committed cut of every authoritative resource needed to restore that state. Derived indexes may use a documented rebuild recipe. Restoring an older cut starts with delivery paused and a new store incarnation until reviewed and resumed.

## Rationale and consequences

Separate lifecycles prevent log compaction from changing customer-visible history and make restore scope explicit. A backup is not valid if it omits metadata required to interpret data or if it silently replays external effects. Retention pins may consume storage; physical purge across the ReplicaCount copies, snapshots and archives needs its own measured and operational contract.

## Related requirements

StorageRecovery `REQ-STORAGE-004`, EventStreams `REQ-EVENT-005..007`, and Messaging `REQ-MSG-006`. Related files: [StorageRecovery](../Features/StorageRecovery.md) and [BackupRestore](../Features/BackupRestore.md).

## Implementation contract

1. Freeze authoritative manifest, committed store cut, retention pins, event-history guarantees, paused restore, and external-effect reconciliation.
2. Add real-store tests for current-format backup consistency, interruption, corruption, retention pins, restore-old-cut token invalidation, and absence of automatic dispatch.
3. Implement in `src/KeyLoad.Core/Restore/` and `src/KeyLoad.Storage.ZoneTree/Restore/`; each silo-local ZoneTree owner backs up and restores its own local files.
4. Validate the current manifest version and every resource before restore. Reject unsupported versions and preserve original archive bytes; stop dispatch if any resource/cut is unknown.
5. GitHub CI runs TUnit, recovery, and replicated-cluster restore drills.

Dependencies: [ADR-003](ADR-003-durability-ack-barrier.md), [ADR-004](ADR-004-committed-read-views.md), [ADR-005](ADR-005-canonical-keyspace-codec.md), and [ADR-030](ADR-030-retention-paused-restore.md). Stop if any authoritative message/history state is omitted or restore would automatically invoke an external side effect.

Capture requires the complete current-format set of the store's live partition families; canonical data records remain authoritative. A partial capture is never reinterpreted as ready, and rollback preserves the original rows and withholds capture (REQ/AC-BACKUP-004 in [BackupRestore](../Features/BackupRestore.md)).

```mermaid
flowchart LR
    Sources[Canonical data events queue and cursor state] --> Cut[Consistent catalog and partition cuts]
    Cut --> Manifest[Versioned authoritative manifest]
    Manifest --> Validate[Verify resources and checksums]
    Validate --> Restore[Restore paused with new incarnation]
    Restore --> Review[Reconcile external outcomes]
    Review --> Resume[Explicit authorized dispatch resume]
```
