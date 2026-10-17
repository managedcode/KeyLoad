# ADR-008: Backup cuts and independent log retention

Status: Accepted; full backup/restore and cluster qualification pending. Product source: [sections 5–6 and 37–45](../design/architecture-v0.3.uk.md); source test status is tracked by [StorageRecovery](../Features/StorageRecovery.md) and peer-owned BackupRestore documentation.

## Context and decision

WAL/transaction journals, domain event history, queue state, subscription checkpoints, inbox outcomes, and rebuildable projections have different authority and retention. Trimming a recovery log must not silently delete public event history. A backup records an internally consistent catalog and per-atomic-partition cuts for every authoritative resource needed to restore that state. Derived indexes may use a documented rebuild recipe. Restoring an older cut starts with delivery paused and a new store incarnation until reviewed and resumed.

## Rationale and consequences

Separate lifecycles prevent log compaction from changing customer-visible history and make restore scope explicit. A backup is not valid if it omits metadata required to interpret data or if it silently replays external effects. Retention pins may consume storage; physical purge across the ReplicaCount copies, snapshots and archives needs its own measured and operational contract.

## Related requirements

StorageRecovery `REQ-STORAGE-004`, EventStreams `REQ-EVENT-005..007`, and Messaging `REQ-MSG-006`. Related files: [StorageRecovery](../Features/StorageRecovery.md), and planned [BackupRestore](../Features/BackupRestore.md).

## Implementation contract

1. Freeze authoritative manifest, per-partition cut, retention pins, event-history guarantees, paused restore, and external-effect reconciliation.
2. Add real-store tests for current-format backup consistency, interruption, corruption, retention pins, restore-old-cut token invalidation, and absence of automatic dispatch.
3. Implement in `src/KeyLoad.Core/Restore/` and `src/KeyLoad.Storage.ZoneTree/Restore/`; each silo-local ZoneTree owner backs up and restores its own local files.
4. Validate the current manifest version and every resource before restore. Reject unsupported versions and preserve original archive bytes; stop dispatch if any resource/cut is unknown.
5. GitHub CI runs TUnit, recovery, and replicated-cluster restore drills. Root owns the manifest/cut contract; Messaging/EventStreams join with checkpoint/inbox/retention proof and exact artifacts.

Dependencies: [ADR-003](ADR-003-durability-ack-barrier.md), [ADR-004](ADR-004-committed-read-views.md), [ADR-005](ADR-005-canonical-keyspace-codec.md), and [ADR-030](ADR-030-retention-paused-restore.md). Stop if any authoritative message/history state is omitted or restore would automatically invoke an external side effect.

## TASK-KL042-CATALOG-001A implementation contract

The ordered roster foundation under REQ/AC-BACKUP-004 is frozen in
[BackupRestore](../Features/BackupRestore.md). Add stable generated native entry
contracts and canonical full-scope keys, then integrate actual staged-write
registration with command effects/outcomes and commit validation, including
Reset/rejection and cross-partition destination writes. Author
the real ZoneTree command/reopen/retry/rejection/deletion flows in the same stage.
Root owns shared joins and native gates. No public backup API,
startup behavior, or dependency changes are included here.

A complete current-format roster of live partition-family keys is required
before capture admission. Canonical data records remain authoritative. Preserve
original rows on rollback and withhold capture; never reinterpret a partial
roster as ready. New records use their frozen aliases/IDs and a homogeneous
current release. Full build/format, Aspire unit/scalar/recovery/replicated-cluster
tests, native functional coverage and exact-source Linux proof remain required.
This stage and ADR remain unqualified until their mapped capture/restore
operations meet acceptance.

```mermaid
flowchart LR
    Sources[Canonical data events queue and cursor state] --> Cut[Consistent catalog and partition cuts]
    Cut --> Manifest[Versioned authoritative manifest]
    Manifest --> Validate[Verify resources and checksums]
    Validate --> Restore[Restore paused with new incarnation]
    Restore --> Review[Reconcile external outcomes]
    Review --> Resume[Explicit authorized dispatch resume]
```

## TASK-KL098-MIXED-RETAINED-ARCHIVE-001 traceability join

REQ/AC-BACKUP-003/004 and REQ/AC-EVENT-RETENTION-001–005 map to the exact current-format mixed-cut operation, ordering, native source owners, higher-epoch privacy repair and remaining qualification in [BackupRestore](../Features/BackupRestore.md#task-kl098-mixed-retained-archive-001--current-format-common-eventing-cut). Existing original local artifact, Phase1 queue backup, target-inbox cold/process and topic purge/process cases remain unchanged. The proposed new Unit case creates real original subscription gap/inbox, pending/parked/leased queue, target inbox/effects and separately purged source under ONE original native archive cut; no empty-family count or fake rows. Both actual atomic partitions, immutable outcomes and complete source/archive bytes stay exact. Paused new-incarnation restore, original authority refusal, explicit authorized continuation, current persisted privacy/revoke/repair and joined same-root cold/healthy follow.

Ordered join is this docs contract before new Unit BackupRestore Cases/Contracts/Models/Helpers/Assertions, then root native preview/build/normal+scalar discovery and original Linux source/DLL/PDB/images. No product/schema/public/family/option/default/timeout/clock change. Rollback removes this test-only addition and append, preserving original source/history. Actual 67 inventory and one source declaration are not native UIDs/PASS. Mixed SDK/official MCP and process gates, receipt horizon/rebuild, and physical-erasure/endurance/power-loss requirements remain OPEN.
