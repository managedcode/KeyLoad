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
2. Add real-store tests for current-format backup consistency, interruption, corruption, retention pins, restore-old-cut token invalidation, and absence of automatic dispatch; qualify replica erase/reinstall separately.
3. Implement in `src/KeyLoad.Core/Features/BackupRestore/`, `src/KeyLoad.Storage.ZoneTree/Features/BackupRestore/`, and replication snapshot ownership under `src/KeyLoad.Replication/Features/ClusterReplication/`.
4. Validate the current manifest version and every resource before restore. Reject unsupported versions and preserve original archive bytes; stop dispatch if any resource/cut is unknown.
5. GitHub CI runs TUnit, recovery, and RF3 restore drills. Root owns the manifest/cut contract; Messaging/EventStreams join with checkpoint/inbox/retention proof and exact artifacts.

Dependencies: [ADR-003](ADR-003-durability-ack-barrier.md), [ADR-004](ADR-004-committed-read-views.md), [ADR-005](ADR-005-canonical-keyspace-codec.md), [ADR-007](ADR-007-replica-consensus-bootstrap.md), [ADR-016](ADR-016-atomic-physical-placement.md), and [ADR-030](ADR-030-retention-paused-restore.md). Stop if any authoritative message/history state is omitted or restore would automatically invoke an external side effect.

## TASK-KL042-CATALOG-001A implementation contract

The ordered roster foundation under REQ/AC-BACKUP-004 is frozen in
[BackupRestore](../Features/BackupRestore.md). Add stable generated native entry
contracts and canonical full-scope keys, then integrate actual staged-write
registration with command effects/outcomes and commit validation, including
Reset/rejection, empty placement and cross-partition destination writes. Author
the real ZoneTree command/reopen/retry/rejection/deletion flows in the same stage.
Root owns shared joins and native gates; Luna produces a bounded private source
packet in the exact feature-local paths specified there. No public backup API,
startup behavior, physical topology, or dependency changes are included here.

The current one-physical-shard RF3 cut is shared by logical partitions. A complete
catalog requires a later fenced, resumable RF3 census before capture admission;
the current roster comprises live partition-family keys and explicit placement
rows. A complete current-format census is required before capture admission.
Canonical data and ownership records remain authoritative. Preserve original rows
on rollback and withhold cluster capture; never reinterpret a partial roster as
ready. New records use their frozen aliases/IDs and a homogeneous current
release. Full build/format, Aspire unit/scalar/recovery/RF3, native functional
coverage and exact-source Linux proof remain required. This stage and ADR remain
unqualified until their mapped capture/restore operations meet acceptance.

```mermaid
flowchart LR
    Sources[Canonical data events queue and cursor state] --> Cut[Consistent catalog and partition cuts]
    Cut --> Manifest[Versioned authoritative manifest]
    Manifest --> Validate[Verify resources and checksums]
    Validate --> Restore[Restore paused with new incarnation]
    Restore --> Review[Reconcile external outcomes]
    Review --> Resume[Explicit authorized dispatch resume]
```
