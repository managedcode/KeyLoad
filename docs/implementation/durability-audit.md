# Kernel durability boundary

This audit describes the initial format, not power-loss qualification. Package versions are pinned centrally in `Directory.Packages.props`. The server uses independent `KeyLoad:SiloCount` and `KeyLoad:ReplicaCount` startup options; the embedded adapter is used for local testing, administrative tools and benchmarks.

## Canonical storage

ZoneTree is the ordered materialization. Its data WAL is synchronous and compression is disabled in the initial profile. KeyLoad's checksummed `commands.wal` is authoritative for recovery: it stores the exact compiled mutations, including the outcome, apply watermark, indexes and queue state. Recovery does not reevaluate preconditions against a partly materialized transaction.

The adapter compiles each operation under a write gate, writes a bounded frame, calls `Flush(true)`, applies all mutations to ZoneTree and releases the gate. Readers hold a read gate across their point/index scans and projection. A failure after append makes the open adapter unusable until recovery. An incomplete final frame is truncated; a complete frame with a checksum or sequence error fails closed. Only a successful recovery permits service to resume.

Staging accounts canonical base64 frame bytes incrementally, including replacements and deletions, before copying additional mutations. Compilation also validates the exact serialized frame before any journal write and reuses that prepared payload during commit. If domain/index/outbox expansion exceeds the frame limit, the state machine resets staged effects and persists a bounded `ResourceExhausted` outcome, clock and apply position instead. The rejection therefore advances the apply position and survives retry/reopening. Nullable array elements and undefined mutation/catalog enums are rejected before dereference. A mutation's declared resource must match its canonical collection/stream/queue/graph owner before authorization; shadowed JSON properties cannot select a different grant. Malformed client data cannot become an unapplicable stored entry through those validated paths.

The separate node ownership lock prevents concurrent writers. Returned bytes and staged mutations are copied. Identity and backup files are versioned; the backup manifest verifies every canonical file with SHA-256. A backup is made under the apply gate and reports that exact cut. Restore explicitly persists paused dispatch even if the old journal had unpaused it.

Format 2 adds a checkpoint prefix with ordered live records, per-frame SHA-256 checksums and a complete-image checksum/count. Offline compaction preserves the logical cut and cursor generation. Process-kill regressions cover checkpoint write/flush and its publication boundaries, including reclamation after reopening.

## Replicated acknowledgement

Orleans does all clustering ([ADR-136](../ADR/ADR-136-orleans-partition-ownership.md)). Each silo's silo-local Grain Service maintains that node's ZoneTree. The writer finds the target silos through Orleans membership, sends the write as Grain Service calls to the `ReplicaCount` target silos; the write is acknowledged after every configured copy confirms durable local apply ([StorageReplication](../Features/StorageReplication.md), [ADR-003](../ADR/ADR-003-durability-ack-barrier.md)). Cancellation or response loss after admission has an unknown write outcome; retries must retain the command ID. Every configured copy must acknowledge before a write succeeds.

Per-source subscriptions journal bounded delivery references, contiguous checkpoints and signed lease generations. Processing effects, inbox receipts, ACK and the command outcome share one compiled redo frame. Seven process-kill scenarios reopen the real store before retrying a processing command and require the effect and checkpoint to agree; a second inbox retry must not apply the effect twice. Seek is an explicit generation fence. Retained topic quotas fail the producer batch before publication; reclamation and cross-partition coverage are separate pending work.

The committed system outbox stores accepted mutations and document before/after images alongside their effects. A generation-bound projection batch commits canonical effects, its replay receipt, checkpoint and resulting outbox entries atomically. The outbox has no producer quota. Seven additional process-kill scenarios require checkpoint, effect, outbox tail and command outcome to agree after reopening, then replay without duplicating the effect. Active projection and rebuild consumers pin retention. Public document feeds and scalar live-query deltas use current row/field permissions; ACL changes fence old cursors. The initial live snapshot and captured tail share one read gate.

Text and vector search, SQL predicates, row authorization and returned field projection each stay within one storage read gate.

Search and graph reads charge raw scan keys/values and all document/edge point dereferences against one cumulative read budget. Hybrid modalities share that budget. Cancellation and a monotonic deadline bound execution between operations. Graph visit caps include filtered/hidden adjacency work across vertices; serialized result caps include metadata. Exact BM25 retains query-term counts with a corpus-wide token ceiling rather than retaining every corpus word. These read bounds do not change canonical writes or establish a total execution/RSS limit; see `docs/design/bounded-reads.md`.

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
