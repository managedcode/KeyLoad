# ADR-136: Orleans execution and physical storage ownership

Status: Accepted design. Both storage modes and partition-query dispatch
are in source; their complete runtime and delivered-source qualification is open.

## Decision

Microsoft Orleans owns membership, discovery, failure detection, the distributed
grain directory, placement, activation movement, scheduling, communication and
scaling. KeyLoad uses the distributed directory and activation
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

`KeyLoad:StorageMode` selects `IsolatedVolumes` by default or `SharedVolume`.
`KeyLoad:SiloCount` and `KeyLoad:ReplicaCount` remain explicit positive settings;
copies cannot exceed silos. `SharedVolume` requires `ReplicaCount=1` and rejects
other copy counts rather than changing them. The Aspire host supplies
`DataRoot/<silo-name>` for isolated storage and the same `DataRoot/shared` path
to every shared-storage node. It forwards the selected mode to each server.
Comparison scenarios retain their required copy counts and reject a conflicting
shared-volume selection.

A shared mount makes files reachable; it does not make ZoneTree a concurrent
multi-process writable database. The pinned ZoneTree WAL uses exclusive file
sharing. Its mutable engine state and KeyLoad's apply gates are process-local.
The directory coordinates the logical grain and the real filesystem lock
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
multi-stage session or response-stream enumerator therefore belongs to an
ordinary identity or its actual storage owner, not to a worker-pool reference.

Grain Services own per-silo resources. Their typed clients address actual execution
silo generations obtained from Orleans membership. Local code borrows the owner
through DI. A timer flushes an activation-local buffer at its typed interval;
that buffer is disposable and does not itself prove durable delivery. Work,
cancellation, pins and buffers remain owned until their actual operations
and cleanup join. A canceled caller Task is not remote completion evidence.

All operations enforce fresh persisted authorization, bounded fan-out and
retained results, cancellation, idempotent command effects and telemetry.

## Current implementation and verification

In isolated mode, the database commit grain selects actual silo addresses
and sends one prepared command to its configured storage copies. Reads select an
available canonical copy. These are full database copies; query fan-out does not
turn them into separate data shards.

In shared mode, the ordinary database grain acquires one local physical session
during activation. The silo-local owner opens the configured files and binds
scoped database, search, maintenance and analytics services to that exact session.
Other silos forward through their Grain Service to the ordinary owner. Accepted
operations await their original local Tasks, with call-chain reentrancy
for callbacks. Deactivation joins the physical work ledger and scoped borrowers
before closing the files. Activation failure after acquisition also joins that
same session's close. An RPC timeout alone never authorizes releasing files.

`PartitionQuery` and `DistributedSearch` address ordinary partition query grains
through the shared identity helper. Each request resolves and retains one execution
silo address and physical identity per distinct logical partition, then uses
that owner for its bounded leaf stages. Returned owner, incarnation, cut and
resource accounting are validated before aggregation. Replica stores are not
extra logical partitions. Workers and durable coordinators obtain bounded typed
reads from the same storage owners instead of capturing a database in their
constructors. Verification limits remain explicit in
[status.json](../implementation/status.json).

Qualify both storage modes with real SDK/MCP operations, actual file identities,
concurrent requests, duplicate commands, fresh authorization, cancellation,
owner loss, reopening and joined shutdown. Shared-volume tests must use the
actual volume's locking behavior; isolated-volume tests must demonstrate genuine
partition fan-out without counting copies twice. Full build, TUnit, process
recovery, actual Aspire cluster operations and exact-source Linux qualification
are separate gates. Process-kill recovery does not prove power-loss durability.

## references

- [Grain directory](https://learn.microsoft.com/en-us/dotnet/orleans/host/grain-directory)
- [Grain placement and activation repartitioning](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-placement)
- [Stateless worker grains](https://learn.microsoft.com/en-us/dotnet/orleans/grains/stateless-worker-grains)
- [Grain Services](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grainservices)
- [Pinned ZoneTree WAL opening](https://github.com/ZoneTree/ZoneTree/blob/dea002a935e0de8ed6bae98b526556c440b6b467/src/ZoneTree/WAL/Sync/SyncFileSystemWriteAheadLog.cs)
