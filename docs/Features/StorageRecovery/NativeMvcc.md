# Native MVCC storage

[StorageRecovery](../StorageRecovery.md) owns this integration of published
[ZoneTree 2.0.6](https://github.com/ZoneTree/ZoneTree/releases/tag/release-v2.0.6).
The silo-local physical owner opens one `ITransactionalZoneTree` through
`OpenOrCreateTransactional` or the existing-store `OpenTransactional` operation.
The current data epoch is 8, including internal grain request and runtime-journal
purposes. Opening requires the current identity before the engine opens its files.

REQ-STORAGE-MVCC-001 / AC-STORAGE-MVCC-001: each prepared KeyLoad command frame is
flushed to the durable command journal before one native transaction applies all
of its mutations and commits. The publication gate ties that commit to the local
position and disposable cache invalidation. An interrupted publication retains
the existing unknown-outcome and replay contract. The operation compiler retains
its bounded frame validation and per-action preparation semantics.

The command journal supplies acknowledgement durability and ordered recovery.
The native tree's WAL is disabled; `Commit()` supplies atomic visibility, while
recovery reapplies each complete journal frame in one native transaction. A
checkpoint first removes the existing logical image inside its native transaction,
then applies the complete checkpoint before publishing its position. Keys absent
from the checkpoint cannot survive in a previously merged disk segment.
Native merge maintenance owns physical version retention. This contract does not
claim power-loss qualification.

REQ-STORAGE-MVCC-002 / AC-STORAGE-MVCC-002: reads use native read-only transactions.
Range cursors and retained read cuts own both the snapshot transaction and its
iterator. Iterator disposal precedes transaction disposal, and an unsettled owner
retains its slot and files. Existing record, byte, elapsed, cancellation and
joined-shutdown limits remain mandatory. Later commits cannot change an earlier
snapshot's point or range results.

REQ-STORAGE-MVCC-003 / AC-STORAGE-MVCC-003: KeyLoad passes logical value bytes
directly to the transactional API. Native MVCC owns deletion records and physical
version encoding. Empty values and values beginning with any byte remain values.
Backup/checkpoint records contain the same logical keys and payloads.

REQ-STORAGE-MVCC-004 / AC-STORAGE-MVCC-004: after a real merge places original
records on disk, deletion followed by compaction and two cold opens must preserve
the exact remaining rows, identity and position. A healthy command between those
opens must persist, including an empty value.
`CheckpointTests.DiskDeletionAndCompactionKeepExactRowsAcrossTwoReopens` exercises
that complete operation and rejects resurrection of a deleted disk key.

`NativeMvccStorageTests` exercises the actual product tree factory, uncommitted
visibility, commit, disposal rollback, stable snapshots, exact binary/empty
values, serializable range conflicts and healthy subsequent commits. Existing
`TombstoneValueTests`, read-cut, range, checkpoint, backup and real process
recovery cases exercise complete KeyLoad operations. Exact-source Linux
functional, process and replicated SDK/MCP qualification and released-image
benchmarks remain separate gates.
