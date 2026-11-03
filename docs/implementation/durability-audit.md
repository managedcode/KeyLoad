# Kernel durability boundary

This audit describes the current format, not power-loss qualification. Package versions are pinned centrally in `Directory.Packages.props`. The server uses independent `KeyLoad:SiloCount` and `KeyLoad:ReplicaCount` startup options; the embedded adapter is used for local testing, administrative tools and benchmarks.

## Canonical storage

KeyLoad owns no write-ahead log, backup, snapshot or archive format ([ADR-011](../ADR/ADR-011-current-format.md)). A store contains `owner.lock`, the checksummed `identity.json`, the ZoneTree MVCC tree and the direct-resource tree. Native WAL uses uncompressed `Shared` for MVCC and `SyncCompressed` for direct data. A transactional commit holds its compiled mutations, outcome, apply watermark, indexes, queue state and `system/position` in one ZoneTree transaction. Recovery replays the engine's log without reevaluating preconditions.

The adapter uses native snapshots and serializable conflict retries without an application writer gate. Direct-resource writes publish through their native WAL before the transactional outcome; their multi-record effects are not atomic. Successful acknowledgements wait for process-persistent publication, without requesting a disk sync per operation. `IAtomicStore.SyncAsync` explicitly awaits `SyncWALAsync`; the engine owns periodic sync, and disposal joins native work. An ordinary completed commit can precede disk sync. Process-kill recovery and power-loss durability are distinct guarantees. A failed publication or sync makes the adapter unusable until successful reopen. See [StorageRecovery](../Features/StorageRecovery.md) and the [native measurements](zonetree-native-measurements.md).

A commit carries no frame or byte limit. Nullable array elements and undefined mutation/catalog enums are rejected before dereference. A mutation's declared resource must match its canonical collection/stream/queue/graph owner before authorization; shadowed JSON properties cannot select a different grant. Malformed client data cannot become an unapplicable stored entry through those validated paths.

The separate node ownership lock prevents concurrent writers. Returned bytes and staged mutations are copied. The identity file is versioned and checksummed. A backup is the identity file plus one ZoneTree live backup generation of the tree; it holds every commit through the position it returns. Restore writes a new identity, restores the tree with ZoneTree and commits paused dispatch even if the backed-up store had resumed it.

Process-kill regressions stop a real child at three stages: after a mutation was staged (`MutationApplied`, recovered absent), after the transaction committed (`ApplyCompleted`, recovered wholly present or wholly absent) and after the sync (`WriteAheadLogSynced`, recovered present).

## Replicated acknowledgement

Orleans does all clustering ([ADR-136](../ADR/ADR-136-orleans-partition-ownership.md)). Each silo's silo-local Grain Service maintains that node's ZoneTree. The writer finds the target silos through Orleans membership, sends the write as Grain Service calls to the `ReplicaCount` target silos; the write is acknowledged after every configured copy confirms its process-persistent local apply ([StorageReplication](../Features/StorageReplication.md), [ADR-003](../ADR/ADR-003-durability-ack-barrier.md)). Cancellation or response loss after admission has an unknown write outcome; retries must retain the command ID. Every configured copy must acknowledge before a write succeeds.

Per-source subscriptions journal bounded delivery references, contiguous checkpoints and signed lease generations. Processing effects, inbox receipts, ACK and the command outcome share one ZoneTree transaction. Five process-kill scenarios reopen the real store before retrying a processing command and require the effect and checkpoint to agree; a second inbox retry must not apply the effect twice. Seek is an explicit generation fence. Retained topic quotas fail the producer batch before publication; reclamation and cross-partition coverage are separate pending work.

The committed system outbox stores accepted mutations and document before/after images alongside their effects. A generation-bound projection batch commits canonical effects, its replay receipt, checkpoint and resulting outbox entries atomically. The outbox has no producer quota. Five additional process-kill scenarios require checkpoint, effect, outbox tail and command outcome to agree after reopening, then replay without duplicating the effect. Active projection and rebuild consumers pin retention. Public document feeds and scalar live-query deltas use current row/field permissions; ACL changes fence old cursors. The initial live snapshot and captured tail share one read gate.

Text and vector search, SQL predicates, row authorization and returned field projection each stay within one storage read gate.

Search and graph reads run raw scans and all document/edge point dereferences under one operation; hybrid modalities share it. It counts no read bytes; cancellation and a monotonic deadline bound execution between operations. Graph visit caps include filtered/hidden adjacency work across vertices; serialized results have no byte cap. Exact BM25 retains query-term counts with a corpus-wide token ceiling rather than retaining every corpus word. These read bounds do not change canonical writes or establish a total execution/RSS limit; see `docs/design/bounded-reads.md`.

The server applies no command or request admission ([ADR-145](../ADR/ADR-145-no-request-throttling.md)): no node, tenant or principal pool counts or reserves commands, requests or bytes, so overload appears as latency rather than refusal. Kestrel enforces one server-wide request body limit (`KeyLoad:ServerExecution:MaximumBodyBytes`) for declared and chunked bodies on every route; an oversize request returns typed `ResourceExhausted` (HTTP 429) without claiming the command ID, and a replicated-cluster test then commits a control command under each rejected ID. Total allocations, process RSS and disk budgets remain pending qualification.

Grains route commands; the silo-local Grain Service that maintains the node's ZoneTree owns files and durability.

## Outstanding qualification

The current source is under development verification. Original Linux reports for
the delivered revision must establish local process recovery, actual Aspire
SDK/MCP faults, complete unit coverage and endurance separately. The presence of
fault stages or scenarios in source does not establish that they passed.

The runtime distinguishes `Buffered`, `ProcessDurable` and `LocalDurable`, with
actual persisted copy counts reported independently. Device flush, directory
persistence, storage-controller behavior and power-loss guarantees remain
unqualified by process-kill tests. See [status.json](status.json) for the open
gates; original evidence belongs to its exact GitHub source and run.
