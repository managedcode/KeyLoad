# Kernel durability boundary

This audit describes the initial format, not power-loss qualification. Package versions, source commits and license identifiers are recorded in `dependency-survey.json`. The server uses RF3 from its first implementation; the embedded adapter is used for local testing, administrative tools and benchmarks.

## Canonical storage

ZoneTree 1.9.8 is the ordered materialization. Its data WAL is synchronous and compression is disabled in the initial profile. KeyLoad's checksummed `commands.wal` is authoritative for recovery: it stores the exact compiled mutations, including the outcome, apply watermark, indexes and queue state. Recovery does not reevaluate preconditions against a partly materialized transaction.

The adapter compiles each operation under a write gate, writes a bounded frame, calls `Flush(true)`, applies all mutations to ZoneTree and releases the gate. Readers hold a read gate across their point/index scans and projection. A failure after append makes the open adapter unusable until recovery. An incomplete final frame is truncated; a complete frame with a checksum or sequence error fails closed. Only a successful recovery permits service to resume.

The separate node ownership lock prevents concurrent writers. Returned bytes and staged mutations are copied. Identity and backup files are versioned; the backup manifest verifies every canonical file with SHA-256. A backup is made under the apply gate and reports that exact cut. Restore changes the incarnation, invalidates old signed tokens, resets the Raft/Orleans watermark and membership, and explicitly persists paused dispatch even if the old journal had unpaused it.

## Replicated acknowledgement

The .NEXT 6.8.1 persistent Raft log owns terms, votes, log matching and consensus commit. `DurableRaftLog` reimplements all public append paths on the native persistent log and awaits `FlushAsync` before an AppendEntries acknowledgement can be returned. `FlushOnCommit` alone would not establish this follower append boundary. A regression invokes append through the actual `IPersistentState` interface and reopens an uncommitted tail.

The bounded leader writer replicates a trusted operation with one leader-chosen evaluation time. It returns success after consensus commit and local state-machine apply, then resolves the persisted outcome under current authorization. Cancellation or response loss after admission has an unknown write outcome; retries must retain the command ID. A minority cannot acknowledge writes.

Strong reads on a leader force a quorum barrier tied to that leadership term. Followers request this barrier through an authenticated peer endpoint and wait for local application of the returned committed position. The provider's follower synchronization API alone is not used as KeyLoad's strong-read acknowledgement. Text and vector search, SQL predicates, row authorization and returned field projection each stay within one storage read gate.

Orleans membership is a replicated catalog record, bootstrapped through consensus before Orleans starts. Grains route commands; they do not own files or durability. The first topology contains one physical shard and many separately scoped atomic partitions.

## Verified and outstanding qualification

Local tests run on macOS arm64 with .NET SDK 10.0.401. The recovery suite executes 1000 seeded real-process kills before/after journal flush and at partial materialization points, verifies atomic recovery, rejects complete-frame corruption and restores a verified backup. The RF3 Aspire suite owns three independent node processes, kills the leader, retries a committed command, checks inbox/effects/ACK, rejects minority operations and restarts voters.

The local result and crash-stage distribution are recorded in `kernel-qualification.json`. Each trial writes its seed, fault stage, mutation index, recovered values and platform to `artifacts/qualification/crash-trials-*.jsonl`; CI retains these as run artifacts.

The advertised profiles remain `ProcessDurable` and `QuorumProcessDurable`. These tests do not establish filesystem directory-entry persistence, storage-controller guarantees, real power-cut behavior or every operating system/filesystem combination. Linux/macOS/Windows CI, network faults and subsequent snapshot/compaction qualification are tracked separately. Power-loss qualification, network-partition coverage, long histories, shard movement and the 72-hour endurance gate remain required for the broader architecture's durable release profile.
