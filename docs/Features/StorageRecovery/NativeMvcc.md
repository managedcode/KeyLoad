# Native MVCC storage

[StorageRecovery](../StorageRecovery.md) owns this integration of the published
ZoneTree package centrally pinned in `Directory.Packages.props`.
The silo-local physical owner opens one `ITransactionalZoneTree` through
`OpenOrCreateTransactional` or the existing-store `OpenTransactional` operation.
The current data epoch is 8, including internal grain request and runtime-journal
purposes. Opening requires the current identity before the engine opens its files.

REQ-STORAGE-MVCC-001 / AC-STORAGE-MVCC-001: each prepared KeyLoad commit is one
native transaction that applies all of its mutations and the store position record
`system/position`, then commits. The position is part of the same native
transaction; native serializable conflicts retry the complete effect-free body. An interrupted publication retains
the existing unknown-outcome and replay contract. The operation compiler retains
its per-action preparation semantics.

ZoneTree's write-ahead log, in `SyncCompressed` mode, is the only redo log and
supplies ordered recovery: on open ZoneTree replays it and drops a transaction whose
commit never reached the log ([ADR-011](../../ADR/ADR-011-current-format.md)).
Native commit supplies atomic visibility and synchronous process-persistent WAL
publication before acknowledgement. ZoneTree owns periodic disk sync;
`IAtomicStore.SyncAsync` awaits `SyncWALAsync` for an explicit disk barrier.
Ordinary commits do not request a disk sync for each command or batch. Native
merge maintenance owns physical version retention. This contract does not claim
power-loss qualification.

REQ-STORAGE-MVCC-002 / AC-STORAGE-MVCC-002: reads use native read-only transactions.
Range cursors and retained read cuts own both the snapshot transaction and its
iterator. Iterator disposal precedes transaction disposal, and an unsettled owner
retains its iterator and files. Existing record, byte, elapsed, cancellation and
joined-shutdown limits remain mandatory. Later commits cannot change an earlier
transactional snapshot's point or range results. Independent read cuts have no singleton limit.
Plain-table data follows its separate native non-transactional semantics.

REQ-STORAGE-MVCC-003 / AC-STORAGE-MVCC-003: KeyLoad passes logical value bytes
directly to the transactional API. Native MVCC owns deletion records and physical
version encoding. Empty values and values beginning with any byte remain values.
A backup is a ZoneTree live backup of the same versioned tree, so it holds the same
logical keys and payloads.

REQ-STORAGE-MVCC-004 / AC-STORAGE-MVCC-004: after a real merge places original
records on disk, deletion and two cold opens must preserve the exact remaining
rows, identity and position. A healthy command between those opens must persist,
including an empty value.
`DiskDeletionTests.DiskDeletionKeepsExactRowsAcrossTwoReopens` exercises that
complete operation and rejects resurrection of a deleted disk key.

`NativeMvccStorageTests` exercises the actual product tree factory, uncommitted
visibility, commit, disposal rollback, stable snapshots, exact binary/empty
values, serializable range conflicts and healthy subsequent commits. Existing
`TombstoneValueTests`, read-cut, range, backup and real process recovery cases
exercise complete KeyLoad operations. Exact-source Linux functional, process and
replicated SDK/MCP qualification and released-image benchmarks remain separate
gates.

ZoneTree owns periodic cache cleanup and merges through `CreateMaintainer()`.
KeyLoad schedules no second cleanup timer and owns no logical point-value cache.
See [NonTransactionalTables](../RelationalStorage/NonTransactionalTables.md) for the
second native tree, WAL ordering and independent backup cuts.
