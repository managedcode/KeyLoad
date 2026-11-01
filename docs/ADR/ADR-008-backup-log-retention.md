# ADR-008: Backup cuts and independent log retention

Status: Accepted; full backup/restore and cluster qualification pending. Product source: [sections 5–6 and 37–45](../design/product-specification.md); source test status is tracked by [StorageRecovery](../Features/StorageRecovery.md) and [BackupRestore](../Features/BackupRestore.md).

## Context and decision

ZoneTree's write-ahead log, domain event history, queue state, subscription checkpoints, inbox outcomes and rebuildable projections have different authority and retention. ZoneTree owns its log and segment lifecycle; that lifecycle never deletes public event history. A backup of one silo-local store is `identity.json` plus one ZoneTree live-backup generation of the store's MVCC tree ([ADR-011](ADR-011-current-format.md)). It holds every commit through the position `CreateBackupAsync` returns, and with it every authoritative record of that store: documents, events, queue and subscription state, inbox outcomes, stored command outcomes, sequences and clocks. Derived indexes are rebuilt by their owners. Restoring a backup starts with delivery paused and a new store incarnation until reviewed and resumed.

## Rationale and consequences

Separate lifecycles keep ZoneTree maintenance from changing customer-visible history and make restore scope explicit. Because the backup is the store's own tree, no record class can be omitted from it, and restore replays no external effect: dispatch stays paused and the new incarnation fences old tokens and outcomes. Retention pins may consume storage; physical purge across the ReplicaCount copies and backups needs its own measured and operational contract.

## Related requirements

StorageRecovery `REQ-STORAGE-004`, EventStreams `REQ-EVENT-005..007`, and Messaging `REQ-MSG-006`. Related files: [StorageRecovery](../Features/StorageRecovery.md) and [BackupRestore](../Features/BackupRestore.md).

## Implementation contract

1. `IAtomicStore.CreateBackupAsync` writes the backup into an empty directory and returns its position; `ZoneTreeStore.RestoreAsync` restores it into an empty destination under a new identity with dispatch paused; `ZoneTreeBackupCopy` copies a backup into ManagedCode.Storage file storage.
2. Real-store tests cover the backup position, new restored identity, paused dispatch, refused nonempty destinations, cancellation before any destination exists, the file-storage copy, restored retention pins, old-token invalidation and the absence of automatic dispatch.
3. The implementation lives in `src/KeyLoad.Storage.ZoneTree/Restore/`; each silo-local ZoneTree owner backs up and restores its own local files.
4. Restore validates the backed-up identity before it writes anything and rejects an unsupported identity format.
5. GitHub CI runs the TUnit, process-recovery and replicated-cluster suites.

Dependencies: [ADR-003](ADR-003-durability-ack-barrier.md), [ADR-004](ADR-004-committed-read-views.md), [ADR-005](ADR-005-canonical-keyspace-codec.md), and [ADR-030](ADR-030-retention-paused-restore.md). Stop if any authoritative message/history state is omitted or restore would automatically invoke an external side effect.

```mermaid
flowchart LR
    Sources[Canonical data events queue and cursor state] --> Tree[One store MVCC tree]
    Tree --> Backup[identity.json and one live-backup generation]
    Backup --> Restore[Restore paused with new incarnation]
    Restore --> Review[Reconcile external outcomes]
    Review --> Resume[Explicit authorized dispatch resume]
```
