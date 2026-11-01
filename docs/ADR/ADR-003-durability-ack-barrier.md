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

The local barrier of each owner is ZoneTree's write-ahead log
([ADR-011](ADR-011-current-format.md)). A commit is visible when
`IAtomicStore.Commit` returns and durable only after the next
`IAtomicStore.SyncAsync`, which awaits ZoneTree's `SyncWALAsync`. Every
acknowledgement path awaits that sync before it reports success: the silo-local
storage service after a batch, the silo command coordinator, the local storage
commit coordinator and the embedded coordinator. One sync covers every commit
returned before it, so a batch of commands pays one sync per owner. A visible
commit that was not yet synced can be lost by a process crash and is never
acknowledged.

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
