# KeyLoad

KeyLoad is a .NET database built around atomic transaction domains, durable ordered storage, and an RF3 Raft cluster. Documents, event streams, retained topics, subscription groups, work queues, graph edges, samples and vector sidecars share the same transactional command path. Orleans provides the routing layer; each node owns its ZoneTree materialization and journals.

The original load-testing prototype has been replaced. This repository implements the new [architecture and development plan](docs/design/architecture-v0.3.uk.md), with the [original HTML edition](docs/design/architecture-v0.3.uk.html) preserved alongside it.

## Development status

This is an early implementation of the clustered kernel. The default server topology has three persistent voting nodes and requires a majority for writes and strong reads. It needs no external database, Redis or message broker.

Implemented surfaces include document CRUD and field patches, partition-scoped unique/composite equality indexes, command deduplication, stream expected-revision append/read, retained topics, per-source durable subscriptions with bounded delivery windows and contiguous checkpoints, scheduled work queues with fenced leases and retry/DLQ, atomic inbox completion, bounded property-graph traversal, ordered samples, exact vector search, BM25 and weighted reciprocal-rank fusion, a bounded read-only Q1 SQL dialect, API keys, field omission and field-use policies, verified backups, native Raft snapshots and empty-replica catch-up, offline journal compaction, a .NET SDK and CLI.

The [104-task implementation tracker](docs/implementation/status.json) records the remaining work. Automatic canonical journal maintenance, large and interrupted snapshot transfer qualification, shard movement, distributed multi-shard query planning, managed HNSW, subscription coverage discovery across partitions and rebalance, CDC, retention, schema migrations, external backup stores and full release qualification remain under development. The current server uses one replicated physical shard which contains many independent atomic partitions.

The advertised profiles are `ProcessDurable` for embedded storage and `QuorumProcessDurable` for the cluster. The kernel has process-kill recovery tests and the cluster has real leader-loss and minority tests. Power-loss qualification, broader platform qualification and the 72-hour endurance gate are still required before advertising `LocalDurable`, `QuorumDurable` or production readiness.

## Run the RF3 cluster

Install the .NET SDK selected in [global.json](global.json). Package versions are pinned centrally and committed lock files record the resolved dependency graph.

```sh
dotnet restore KeyLoad.slnx --locked-mode
dotnet build KeyLoad.slnx --no-restore
dotnet run --project src/KeyLoad.AppHost
```

Aspire starts `node1`, `node2` and `node3`, checks readiness and collects telemetry. Development HTTP endpoints are `http://localhost:5101`, `http://localhost:5102` and `http://localhost:5103`. Each voter stores its files below `data/cluster/nodeN`. A local profile is generated once at `data/cluster/local-profile.json`; it contains development credentials and is excluded from Git. Keep the profile with the database directories across restarts. Changing voter addresses requires a membership migration.

```sh
dotnet run --project src/KeyLoad.Cli -- status \
  http://localhost:5101 data/cluster/local-profile.json
```

The standalone server requires explicit cluster identity, voter endpoints, shared peer authentication secrets, an administrator credential and a silo port through `KeyLoad` configuration. Set `KeyLoad:SiloAddress` to each node's advertised IP address for a cluster across hosts. HTTPS is required for cluster endpoints; the AppHost explicitly enables loopback HTTP for development. Peer RPCs and forwarding requests are signed for their recipient, path, query, body and Raft protocol headers, timestamped and replay checked. Large peer bodies use bounded temporary files. Public HTTP requests authenticate against the replicated credential catalog. Client-supplied roles are never trusted.

The default peer connection/RPC/request timeouts are 500/1500/2000 milliseconds, below the 4000–8000 millisecond election range. These values are configurable through the corresponding `KeyLoad` options; validation requires that ordering. Native snapshots are produced every `KeyLoad:SnapshotThreshold` committed entries (default 1024). Small snapshot catch-up is tested; large transfers still need qualification against the configured RPC deadline.

## .NET client

The SDK uses `ManagedCode.Communication.Result<T>` and typed protocol records. Supply an API key in application configuration. Keep command IDs stable across retries: an interrupted write response has an unknown outcome.

```csharp
using KeyLoad;
using KeyLoad.Client;

using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5101") };
var client = new KeyLoadClient(http, apiKey);
var partition = new PartitionRef("acme", "shop", "order-processing", "customer-42");

await client.ConfigureResourceAsync(Guid.NewGuid(), new("acme", "shop",
    new ResourceDefinition("orders", ResourceKind.Collection, "order-processing")
    { Indexes = [new("number", ["/number"], Unique: true)] }));
await client.ConfigureResourceAsync(Guid.NewGuid(), new("acme", "shop",
    new ResourceDefinition("events", ResourceKind.StreamSet, "order-processing")));
await client.ConfigureResourceAsync(Guid.NewGuid(), new("acme", "shop",
    new ResourceDefinition("jobs", ResourceKind.WorkQueue, "order-processing")));

var commandId = Guid.NewGuid(); // persist with the producer's work item
var command = new CommandRequest(commandId, partition,
[
    new PutDocument("orders", "order-1", "{\"number\":1,\"status\":\"new\"}", ExpectedRevision: 0),
    new AppendEvents("events", "order-1",
        [new("event-1", "OrderCreated", "{\"number\":1}")], ExpectedStreamRevision.NoStream),
    new EnqueueMessage("jobs", "message-1", "{\"orderId\":\"order-1\"}")
]);
var result = await client.CommitAsync(command);
result.ThrowIfFail();
```

All resources in an atomic batch must be catalog-bound to the same transaction domain and partition. Equal literal partition keys in different domains do not create a shared transaction. Stream revisions start at one; document expected revision zero means create. Reusing a command ID with different content returns a conflict.

```csharp
var lane = new QueueLaneRef(partition, "jobs");
var received = await client.ReceiveAsync(new(Guid.NewGuid(), lane));
received.ThrowIfFail();
foreach (var delivery in received.Value.Deliveries)
{
    var completed = await client.CommitProcessingAsync(new(Guid.NewGuid(), lane,
        delivery.Token, "order-worker", ExecutionGeneration: 1,
        [new PatchDocument("orders", "order-1",
            [new("/status", PatchKind.Set, "\"processed\"")], ExpectedRevision: 1)]));
    completed.ThrowIfFail();
}
```

`CommitProcessing` atomically stores the same-partition effects, inbox receipt and ACK. A stale lease cannot acknowledge a later delivery. External side effects need their own idempotency or outbox integration.

## Retained topics and subscriptions

Topics retain events once per source. Each durable group owns its delivery window, attempts, leases and checkpoint. Groups can also subscribe to one stream generation using `EventSourceKind.Stream` and a stream ID. Source identity includes the atomic partition; cross-partition coverage is still being developed.

```csharp
await client.ConfigureResourceAsync(Guid.NewGuid(), new("acme", "shop",
    new ResourceDefinition("activity", ResourceKind.Topic, "order-processing")));
var source = new EventSourceRef(partition, "activity", EventSourceKind.Topic);
var group = new SubscriptionRef(source, "projection");
await client.ConfigureSubscriptionAsync(new(Guid.NewGuid(), group,
    new SubscriptionDefinition("root"), SubscriptionStart.FromBeginning));

var publish = new CommandRequest(Guid.NewGuid(), partition,
    [new PublishTopic("activity", [new("activity-1", "OrderCreated", "{\"number\":1}")])]);
(await client.CommitAsync(publish)).ThrowIfFail();

var groupReceived = await client.ReceiveSubscriptionAsync(new(Guid.NewGuid(), group, MaxEvents: 10));
groupReceived.ThrowIfFail();
foreach (var delivery in groupReceived.Value.Deliveries)
{
    var processing = new SubscriptionProcessingRequest(Guid.NewGuid(), group, delivery.Token,
        "order-projection", ExecutionGeneration: 1,
        [new PutDocument("orders", "projection-" + delivery.Event.Data.EventId,
            delivery.Event.Data.PayloadJson, ExpectedRevision: 0)]);
    (await client.CommitSubscriptionProcessingAsync(processing)).ThrowIfFail();
}
```

Use the application's data principal for a deployed group; `root` above is the local development principal. Delivery projects the intersection of that data principal's and the worker's current field permissions. A missing required input grant stops delivery. Revocation or a policy epoch change invalidates old payload retries and acknowledgement tokens.

Keep publish, receive and processing IDs stable while resolving an uncertain outcome. Event IDs are unique within a retained topic or stream generation: identical reuse returns `DuplicateEventId`, and changed content returns `Conflict`. Subscription processing stores effects, inbox and ACK in one commit. Replay of the same handler/input returns `AlreadyProcessed` and `OriginalEffectsToken`; a newly leased replay is acknowledged without applying those effects again. A different authorized worker can recover the same inbox outcome after current effect permissions are checked.

ACK only advances a contiguous prefix. ACKs for positions 1 and 3 leave checkpoint 1 until position 2 finishes. `MaxWindow` bounds gaps and active deliveries. NACK uses bounded exponential backoff; exhausted attempts park the unresolved position and pause the group. `SeekSubscriptionAsync` advances its generation, fences previous leases and pauses the group until an explicit resume through `SetSubscriptionPausedAsync`.

`FromNow` records the current source tail inside the creation commit. `FromCursor` uses a signed, principal-bound cursor from `ReadEventSourceAsync`; even an empty page at the tail returns a cursor for later catch-up. Topics enforce retained event/byte quotas and reject the entire producer batch when full. Retention reclamation, group deletion/filter migration and completion-audit compaction remain tracked work.

## Queries and search

```csharp
var page = await client.QueryAsync(new(partition,
    "SELECT o.id, o.status FROM orders o WHERE o.number = 1 ORDER BY o.id LIMIT 20"));
page.ThrowIfFail();
```

Q1 supports projections and aliases, scalar parameters, comparisons, `AND`/`OR`/`NOT`, `IN`, `IS NULL`, `IS MISSING`, `ORDER BY`, `LIMIT` and `EXPLAIN`. Identifiers containing dots need double quotes. `id` and `revision` refer to canonical document identity/revision. Parameters use `@name`. JSON numbers use the decimal scalar policy. SQL is read-only; unsupported statements fail explicitly.

Queries require a matching point/equality index or explicit `AllowFullScan`. Scans, parser depth, tokens, rows, bytes and execution time have budgets. Cursor tokens bind the principal, policy epoch, schema, query, node identity, read generation and current storage cut. Writes, policy changes or snapshot installation can expire a cursor; compaction preserves its cut. Sensitive predicates and sorting require a field-use grant. Returned documents omit protected paths, including classified values nested in arrays.

Search accepts typed vector spaces and explicit text/vector fields. Both branches use one authorized read cut. Exact vector scores and BM25 ranks are combined with weighted RRF using one-based ranks. The managed ANN and graph retrieval extensions are tracked separately.

## Backups and Cartograph

The canonical backup includes the checksummed redo journal, database identity and a SHA-256 manifest. Domain data, schemas, credentials, outcomes and inbox receipts are journaled together. The journal can begin with a verified checkpoint followed by newer transaction frames. ZoneTree files can be rebuilt from that canonical history. Restore validates every manifest file, creates a new incarnation, resets consensus routing metadata and leaves queue dispatch paused.

```sh
# Stop the node before using the offline CLI.
dotnet run --project src/KeyLoad.Cli -- compact data/cluster/node1/database
dotnet run --project src/KeyLoad.Cli -- backup data/cluster/node1/database backups/snapshot
dotnet run --project src/KeyLoad.Cli -- restore backups/snapshot data/restored

dotnet run --project src/KeyLoad.Cli -- pack-backup backups/snapshot backups/snapshot.ctg
dotnet run --project src/KeyLoad.Cli -- inspect-artifact backups/snapshot.ctg
dotnet run --project src/KeyLoad.Cli -- unpack-backup backups/snapshot.ctg backups/unpacked
dotnet run --project src/KeyLoad.Cli -- copy-artifact backups/snapshot.ctg archive-store
```

[Cartograph](https://github.com/angelhernandezm/Cartograph) provides optional memory-mapped archive inspection and bounded file streaming. Large files are split across records. Its published format is experimental (`0.1.0-alpha`), so archives are regenerable transport artifacts; the verified KeyLoad backup remains the restore authority. `ManagedCode.Storage` provides archive transfer. A whole-cluster restore must use one new shared incarnation/signing key and fresh voter metadata; joining an individually restored node to the old cluster is rejected.

## Repository layout

| Project | Responsibility |
| --- | --- |
| `KeyLoad.Abstractions` | Typed protocol, identities, limits, errors and ordered key codec |
| `KeyLoad.Core` | Catalog, atomic mutation compilation, documents, streams, queues, graph and samples |
| `KeyLoad.Storage.ZoneTree` | File ownership, redo recovery, consistent apply gate and verified backups |
| `KeyLoad.Replication` | Durable Raft append barrier, state-machine apply and bounded writer coordination |
| `KeyLoad.Orleans` | Physical-shard command facade and consensus-backed membership |
| `KeyLoad.Security` | Scope, row and sensitive-field policies |
| `KeyLoad.Query` | Bounded SQL, exact vectors, BM25 and fusion |
| `KeyLoad.Server` | HTTP API, authenticated peer transport and bootstrap |
| `KeyLoad.Client` / `KeyLoad.Cli` | .NET SDK and administrative commands |
| `KeyLoad.Artifacts` | Optional Cartograph archive and ManagedCode storage transport |
| `KeyLoad.AppHost` / `KeyLoad.ServiceDefaults` | RF3 Aspire topology, health and OpenTelemetry |
| `tests` / `benchmarks` | Unit/property, crash recovery, real RF3 integration and benchmark workloads |

## Verification

```sh
dotnet build KeyLoad.slnx --no-restore
dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore
dotnet test --project tests/KeyLoad.RecoveryTests --no-build --no-restore
dotnet test --project tests/KeyLoad.IntegrationTests --no-build --no-restore
```

Tests use xUnit and Microsoft.Testing.Platform. Recovery qualification runs 1000 seeded real-process kills, checks complete-frame corruption and verifies a clean backup restore. Additional process kills cover checkpoint publication, native Raft append acknowledgement and subscription effects/inbox/checkpoint publication. Integration tests own the Aspire lifecycle and use three independent server processes with isolated persistent directories. They kill the elected leader, retry the same command, verify atomic effects and topic delivery on surviving voters, reject a minority write and erase/restart one replica to require snapshot catch-up of documents, group checkpoints and inbox outcomes. No manually running AppHost is needed for tests.

Benchmarks can be started with `dotnet run -c Release --project benchmarks/KeyLoad.Benchmarks`. Comparative PostgreSQL/Marten/Wolverine and multi-node scaling qualification are part of the development plan; no comparative performance claims are made yet.

## Dependencies and license

The baseline uses .NET 10, Orleans 10.3.1, Aspire 13.6.0, ZoneTree 1.9.8, .NEXT 6.8.1 and appropriate ManagedCode libraries. See [Directory.Packages.props](Directory.Packages.props) and the [dependency survey](docs/implementation/dependency-survey.json). `ManagedCode.Orleans.Graph` restricts grain call relationships; the property graph lives in KeyLoad's canonical data model.

KeyLoad is released under the [MIT license](LICENSE). Dependency licenses remain with their respective owners. Contribution checks and dependency ownership rules are described in [AGENTS.md](AGENTS.md).
