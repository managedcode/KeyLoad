# ADR-003: Durability profile and acknowledgement barrier

Status: accepted contract; exact-source qualification pending. Source enum:
`src/KeyLoad.Abstractions/DurabilityProfile.cs`. Related specification:
[StorageReplication](../Features/StorageReplication.md),
[StorageRecovery](../Features/StorageRecovery.md) and
[ADR-143](ADR-143-independent-silo-replica-topology.md).

## Decision

The current profiles are `Buffered`, `ProcessDurable` and `LocalDurable`.
`CommitReceipt.Durability` describes the persistence barrier and
`CommitReceipt.PersistedCopies` independently records the number of copies that
actually acknowledged the operation. A profile name never implies extra copies.

`Buffered` permits loss of acknowledged work and reports zero persisted copies.
`ProcessDurable` describes recovery from process termination after the local
barrier. `LocalDurable` may be advertised only after filesystem/device flush
semantics and the required fault evidence establish its stronger contract.

The database grain sends the original command through Orleans to the
configured silo-local ZoneTree owners. Success requires every configured owner
to confirm ordered persisted apply; the returned copy count must match those
actual acknowledgements. Missing copies refuse success. A timeout after possible
effects is an unknown outcome and retains the stable command ID for retry.

The local barrier is ZoneTree 2.1.0's `SyncCompressed` WAL publication
([ADR-011](ADR-011-current-format.md)). Native writes publish into the mapped
local WAL tail before returning. `IAtomicStore.Commit` and `CommitAsync` return
after the native transaction's commit record, so data and persisted outcomes
already survive process termination. Coordinators await that publication and
report `ProcessDurable`; they do not request a disk sync per operation or batch.

ZoneTree owns periodic disk sync. The MVCC tree uses its native
`AutoSyncInterval`, which also retires durable commit state; its lower-level tail
job is disabled to avoid duplicating that work. The plain tree uses its native
tail job. Both use the engine's default interval. `IAtomicStore.SyncAsync` remains
an explicit disk barrier and awaits `SyncWALAsync` for plain data before MVCC
outcomes. Disk sync completion alone does not qualify `LocalDurable` or power-loss
behavior. Backup and restore retain their independently defined barriers.

## Implementation and evidence

Each local atomic transaction records the canonical sequence, result,
original prepared command and business mutations together. Duplicate submission
checks fresh persisted authorization and original content, then returns the
retained outcome without repeating effects. Local derived-index maintenance does
not advance the canonical command cut.

Tests must independently verify records, receipts, copy counts, original outcomes,
resource bounds and healthy follow-up through local process recovery and actual
Aspire SDK/MCP operations. Build, coverage, network faults and endurance remain
separate gates. A process kill establishes no power-loss or storage-controller
guarantee.

Related requirements: Messaging REQ-MSG-001/005, EventStreams REQ-EVENT-004 and
StorageReplication REQ-REP-002/003. Committed read cuts follow
[ADR-004](ADR-004-committed-read-views.md); backup retention follows
[ADR-008](ADR-008-backup-log-retention.md).


Opening checks the actual mutable WAL writer through ZoneTree's public APIs for
both trees before serving operations. Configuration alone does not establish the
publication contract of an existing native WAL. An incompatible opened writer
refuses with `FormatUnsupported`; KeyLoad does not reinterpret the mode or select
another acknowledgement contract at runtime.
