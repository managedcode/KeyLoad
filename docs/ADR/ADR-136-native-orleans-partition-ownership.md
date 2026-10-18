# ADR-136: Orleans execution and physical storage ownership

Status: Accepted design. Shared-volume lifetime and distributed partition-query
execution remain open implementation and qualification gates.

## Decision

Microsoft Orleans owns membership, discovery, failure detection, the distributed
grain directory, placement, activation movement, scheduling, communication and
scaling. KeyLoad uses the native distributed directory and activation
repartitioner. It implements database operations and file ownership through
ordinary grains and silo-local Grain Services.

The request path is public API -> one connection grain per connected client ->
logical database/partition grains -> storage owners -> bounded aggregation ->
caller. Logical atomic partition identity is independent of activation placement
and physical copy identity. A connection owns its response stream and independent
request lifetimes; storage services expose self-contained typed Task calls.

## Storage modes

| Mode | Physical files | Execution and copies |
|---|---|---|
| Shared volume | The same configured volume and physical partition paths are visible to the participating silos. Exactly one open storage owner holds each mutable ZoneTree and its file locks. | An ordinary logical owner grain routes work to that open owner through Orleans. Moving ownership drains borrowers and joins the original close before reopening the same files on another silo. Shared access creates no extra persisted copies and requires no data copying. |
| Isolated volumes | Each silo-local Grain Service owns its configured local files, WAL, locks and ordered apply gate. | Logical partition grains call the required storage owners through Orleans. Optional copies are actual independent persisted stores. A cross-partition query calls each logical partition once and merges bounded partial results. |

A shared mount makes files reachable; it does not make ZoneTree a concurrent
multi-process writable database. The pinned ZoneTree WAL uses exclusive file
sharing. Its mutable engine state and KeyLoad's apply gates are process-local.
The native directory coordinates the logical grain and the real filesystem lock
protects the files. A membership transition cannot release a still-open operating
system handle. An unavailable original owner causes an explicit failure until
the original file ownership has actually ended. Filesystem lock and flush
semantics must be qualified on the deployed volume.

For isolated volumes, grain calls provide the distributed execution path.
Orleans finds activations and transports requests; KeyLoad defines partition
selection, read cuts, query operators and resource limits. Physical replicas of
one partition must not be counted as separate query partitions. A result spans a
vector of partition cuts, rather than an invented global snapshot. Every write
retains its stable command identity and original prepared facts. An
acknowledgement reports the actual persisted copies and qualified durability.

## Workers and lifetime

`[StatelessWorker]` marks interchangeable leaf work or activation-local buffered
pre-aggregation. Orleans creates and retires activations with load. One logical
pool key comes from its identity helper; application code never addresses
individual activations or assumes consecutive calls reach the same one. A
multi-stage session or native response-stream enumerator therefore belongs to an
ordinary identity or its actual storage owner, not to a worker-pool reference.

Grain Services own per-silo resources. Their typed clients address actual native
silo generations obtained from Orleans membership. Local code borrows the owner
through DI. A timer flushes an activation-local buffer at its typed interval;
that buffer is disposable and does not itself prove durable delivery. Work,
cancellation, native pins and buffers remain owned until their actual operations
and cleanup join. A canceled caller Task is not remote completion evidence.

All operations enforce fresh persisted authorization, bounded fan-out and
retained results, cancellation, idempotent command effects and native telemetry.

## Current implementation and verification

The current database commit grain selects actual native silo addresses and sends
one prepared command to its configured storage copies. Reads select an available
canonical copy. This is full-copy routing; it is not distributed partition-query
execution. The current physical host opens one store per silo eagerly. It does
not implement lazy, single-owner shared-volume opening. These limits remain
explicit in [status.json](../implementation/status.json).

Qualify both storage modes with real SDK/MCP operations, actual file identities,
concurrent requests, duplicate commands, fresh authorization, cancellation,
owner loss, reopening and joined shutdown. Shared-volume tests must use the
actual volume's locking behavior; isolated-volume tests must demonstrate genuine
partition fan-out without counting copies twice. Full build, TUnit, process
recovery, actual Aspire cluster operations and exact-source Linux qualification
are separate gates. Process-kill recovery does not prove power-loss durability.

## Native references

- [Grain directory](https://learn.microsoft.com/en-us/dotnet/orleans/host/grain-directory)
- [Grain placement and activation repartitioning](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-placement)
- [Stateless worker grains](https://learn.microsoft.com/en-us/dotnet/orleans/grains/stateless-worker-grains)
- [Grain Services](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grainservices)
- [Pinned ZoneTree WAL opening](https://github.com/ZoneTree/ZoneTree/blob/ec774af2223bd87457a0aa985908f0b4e508e4c8/src/ZoneTree/WAL/Sync/SyncFileSystemWriteAheadLog.cs#L55-L63)
