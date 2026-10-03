# KeyLoad

KeyLoad is an experimental .NET 10 database that puts JSON documents, event streams, durable work queues, graphs, time series and search behind one API and one authorization model. An atomic command can update a document, append its event and enqueue work in the same transaction domain. The first server topology has three replicated nodes; each node owns its ZoneTree storage and journals.

The cluster foundation is Orleans, with separate request grains and a distributed grain directory. Docker/Aspire RF3 operations are exercised through the real .NET and official MCP SDK clients in GitHub Actions. Actual activation migration remains unqualified, and the published comparison baseline predates this replacement. Follow the [architecture map](docs/Architecture.md) and [qualification tracker](docs/implementation/status.json) for the implemented and verified boundaries.

The original load-testing prototype has been replaced. This repository implements the new [architecture and development plan](docs/design/architecture-v0.3.uk.md), with the [original HTML edition](docs/design/architecture-v0.3.uk.html) preserved alongside it.

The [documentation index](docs/README.md) covers all 22 Feature specifications with requirements, acceptance criteria, source/test boundaries and diagrams. The ADR catalog (docs/ADR/README.md) records architectural decisions and implementation contracts; the [coverage map](docs/implementation/documentation-coverage.json) links every KL task to its owning Feature and ADR without changing qualification status.

The product is one database server for AI agents with linked documents, typed relational rows, graphs, vectors/search, files/blobs, events and queues. SQL is the central language. The new [SQL and relational contracts](docs/implementation/central-sql.md) add a versioned SELECT/CALL adapter over the existing operations and schema-constrained canonical rows. Arbitrary SQL JOIN, foreign keys and declarative cross-model SELECT remain required future stages; this source is not yet qualified by an exact-SHA GitHub run.

## Development status

This is an early implementation of the clustered kernel. The default server topology has three persistent voting nodes and requires a majority for writes and strong reads. It needs no external database, Redis or message broker.

Implemented surfaces include document CRUD and field patches, partition-scoped unique/composite equality indexes, command deduplication, stream expected-revision append/read, retained topics, per-source durable subscriptions with bounded delivery windows and contiguous checkpoints, scheduled work queues with fenced leases and retry/DLQ, atomic inbox completion, a committed projection outbox with generation pins, protected resumable document changes and scalar live queries, bounded property-graph traversal, ordered samples, exact vector search, BM25 and weighted reciprocal-rank fusion, a bounded read-only Q1 SQL dialect, API keys, field omission and field-use policies, verified backups, native Raft snapshots and empty-replica catch-up, offline journal compaction, a .NET SDK and CLI.

The additive [TimeSeries reads](docs/Features/TimeSeries.md) provide latest samples, complete half-open raw aggregates and dense UTC windows through typed SDK, HTTP and MCP operations. The current source consumes published `ManagedCode.TimeSeries` 10.0.3 after its owning temporal and summer-allocation repairs: the [delivery receipt](docs/implementation/timeseries-dependency-10.0.3.json) verifies 1107/1107 native library tests, over 90% coverage in each module, four repeated normal/scalar profiles and actual signed NuGet content. Earlier [exact c486 CI](docs/implementation/runtime-qualification-37060131271.json) qualified the 10.0.0 consumer with 871/871 unit cases on each OS and 46/46 genuine RF3 SDK/MCP cases. The updated consumer requires a new exact-SHA run. Retention, rollups, SQL integration and resource qualification remain open.

The latest joined [exact c10c CI](docs/implementation/runtime-qualification-37079707413.json) passes 1088/1088 unit cases in each normal/scalar mode, 136/136 process-recovery cases, 118 analyzer cases and 63/63 genuine RF3 SDK/MCP cases on Linux. All 1,000 seeded process-kill trials preserve atomic cuts; all 66 source-owned WAL cases pass in each mode. Comparison passes 3/4: the pre-cancelled read regression expects an exception while the SDK returns its typed cancellation result. The current source corrects that assertion and requires a new exact-SHA run; two later measured profiles were skipped. This measured source still consumes TimeSeries 10.0.0. Historical [6ad results](docs/implementation/runtime-qualification-37077856823.json), [c062 results](docs/implementation/runtime-qualification-37074392471.json) and [image failure evidence](docs/implementation/image-preflight-failure-37073331174.json) remain available. Coverage thresholds, complete performance leadership and power-loss durability are unqualified.

The current source stage adds common immutable server/runner images, isolated native one/two/three-node CRUD and specialized comparisons, bounded cluster-failure diagnostics and KeyLoad stream readback regressions. Its delivered-SHA GitHub qualification is pending. TimeSeries 10.0.3 removes summer-update allocations for Int32/Int64/Double in its repeated library profiles; complete KeyLoad and Timescale measurements, measured SIMD benefit and Orleans-coordinated RAM caches remain open. Rust requires profiling evidence first.

The [104-task implementation tracker](docs/implementation/status.json) records the remaining work. Automatic canonical journal maintenance, large and interrupted snapshot transfer qualification, shard movement, distributed multi-shard query planning, managed HNSW, subscription coverage discovery across partitions and rebalance, automatic retention and generation cleanup, schema migrations, external backup stores and full release qualification remain under development. The current server uses one replicated physical shard which contains many independent atomic partitions.

The advertised profiles are `ProcessDurable` for embedded storage and `QuorumProcessDurable` for the cluster. The kernel has process-kill recovery tests and the cluster has real leader-loss and minority tests. Power-loss qualification, broader platform qualification and the 72-hour endurance gate are still required before advertising `LocalDurable`, `QuorumDurable` or production readiness.

## Run the RF3 cluster

Install the .NET SDK selected in [global.json](global.json). Package versions are pinned centrally in [Directory.Packages.props](Directory.Packages.props); package lock files are deliberately excluded under current repository policy.

```sh
dotnet restore KeyLoad.slnx
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

Incoming snapshots have a flushed installation intent and complete-image validation. Interrupted, cancelled or invalid transfers preserve the previous committed cut; restart recovery discards an incomplete attempt or installs its complete verified image. Corruption of already materialized snapshots fails closed. See the [replica snapshot recovery contract](docs/design/replica-snapshots.md) for the process-kill checks and remaining fault gates.

Command admission bounds queued and active commands by node count/bytes and verified tenant/principal counts. The default data lane has 256 slots and 128 MiB of retained payload accounting; ACK/renew, membership and dispatch commands have a separate bounded control reserve. A full lane returns `ResourceExhausted` before this attempt's Raft acceptance. Configure `KeyLoad:CommandAdmission` and inspect `AdmissionStatusAsync` as a cluster administrator. See the [admission contract](docs/design/command-admission.md) for defaults, scheduling and the remaining memory/disk qualification work.

Public HTTP admission reserves capacity before JSON deserialization and shares a modeled working budget across query, search and graph reads. Verified tenant/principal counts and a separate delivery/dispatch reserve apply through response processing. Data/control bodies default to 8 MiB/64 KiB; declared and chunked oversize requests return typed `ResourceExhausted`. Configure `KeyLoad:HttpAdmission`; administrator admission status includes its counters. These reservations do not establish a process RSS limit. A voter joining during temporary quorum loss keeps its Raft endpoint available and retries Orleans startup until consensus returns.

## Administration console

The runtime console is served at `/admin` on each database HTTP endpoint, for example `http://localhost:5101/admin` with the local Aspire profile. Connect with a persisted administrator API key, then enter the tenant, database and atomic partition key when browsing collections, queues or published blob metadata. The read-only console shows physical canonical/replica/backup file lengths, node status, admission occupancy and measured process HTTP throughput. It never receives or acknowledges queue messages, and credentials remain only in tab memory. See [AdminDashboard](docs/Features/AdminDashboard.md) for exact GitHub evidence: 13 dashboard unit cases pass on each OS, RF3 passes 36/36 including the real Chrome flow, and retained desktop/mobile images are visually reviewed. The complete workflow still fails independently owned recovery/comparison gates; numeric coverage remains unqualified.

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

SQL, version 1 JSON AST and the C# expression builder normalize into the same typed AST and use the same planner, permissions and execution path. `GET /v1/query/capabilities` (SDK: `QueryCapabilitiesAsync`) reports that contract and the configured limits. JSON requests use `POST /v1/query/ast` or `QueryAstAsync`. See the [Q1 protocol and expression subset](docs/design/query-q1.md).

```csharp
var query = KeyLoadQuery.From<Order>(partition, "orders")
    .Where(order => order.Number == 1m)
    .OrderBy(order => QueryFunctions.DocumentId(order))
    .Select(order => new { Id = QueryFunctions.DocumentId(order), order.Status })
    .Take(20);
var typedPage = await client.QueryAsync(query);
typedPage.ThrowIfFail();

public sealed record Order(decimal Number, string Status);
```

The builder supports scalar comparisons, Boolean composition, constant-array/list `Contains`, null/missing markers, field projections and ordering. It translates expression trees without invoking application delegates or getters. Unsupported methods and lossy casts fail explicitly. It has independent immutable query branches and provides no implicit client evaluation.

Queries require a matching point/equality index or explicit `AllowFullScan`. Scans, parser depth, nodes, parameters, rows, bytes and execution time have budgets. Cursor tokens bind the principal, policy epoch, schema, normalized query, node identity, read generation and persisted collection data version. Equivalent SQL/JSON/C# forms can continue the same cursor. A write to that collection, a principal policy change or snapshot installation can expire it; catalog heartbeats, unrelated collection writes and compaction preserve its cut. Sensitive predicates and sorting require a field-use grant. Returned documents omit protected paths, including classified values nested in arrays.

Search accepts typed vector spaces and explicit text/vector fields. Both branches use one authorized read cut. Exact vector scores and BM25 ranks are combined with weighted RRF using one-based ranks. The managed ANN and graph retrieval extensions are tracked separately.

Search and graph reads share cumulative 64 MiB raw-read accounting across scans and referenced documents/edges, a 30-second execution deadline and request cancellation. Graph edge visits include filtered/hidden candidates across the whole traversal. BM25 retains counts for query terms and enforces a corpus-wide token cap; search/graph response limits include serialized protocol metadata. See the [bounded read contract](docs/design/bounded-reads.md); total process memory qualification remains pending.

## Change feeds and live queries

Every successful mutation appends to a private, per-partition system outbox in the same transaction as its canonical effects and outcome. This is separate from business event streams and retained topics. Public `ReadChangesAsync` returns projected document before/after images and deletion metadata; it requires `ChangesRead` and `DocumentsRead`. Current principal, row visibility and sensitive-field policies are checked on every page.

```csharp
var liveQuery = KeyLoadQuery.From<Order>(partition, "orders")
    .Where(order => order.Status == "new").Take(100).ToRequest(allowFullScan: true);
var initial = await client.StartLiveQueryAsync(new(liveQuery));
initial.ThrowIfFail();
var next = await client.ReadLiveQueryAsync(new(liveQuery, initial.Value.Cursor));
next.ThrowIfFail();
// Apply Upsert/Remove by canonical document ID; retain next.Value.Cursor after applying its page.
```

The initial complete snapshot and change cursor share one read gate. Live predicates use Q1's same validator, field-use binder and scalar evaluator. Deltas are unordered result-set upserts/removals; ranking, top-k, graph and ANN subscriptions are separate profiles. The initial result must fit its row and byte budgets. Delta pages have examined-position and output-byte budgets; the caller bounds its maintained result set. Re-reading a cursor can repeat changes, so apply them by ID/revision. Continue while `HasMore` even if a filtered page is empty.

Cursors bind incarnation, tenant/partition, collection, principal/policy and schema; live cursors also bind the normalized query. A row ACL change requires a fresh snapshot. Lost retention returns `HistoryUnavailable`; invalid policy/scope returns `TokenInvalidated`. Clear the old result set and take a new snapshot in either case. These cursors can continue on another caught-up RF3 voter.

System projection APIs require cluster administration. A consumer defines its index generation and resource/mutation filter. `ReadProjectionAsync` issues a signed contiguous batch; `CommitProjectionAsync` atomically stores same-partition effects, a replay receipt and its checkpoint. Failed effects leave the checkpoint unchanged. A released generation rejects old batch tokens. Active consumer/rebuild checkpoints pin history against `PurgeOutboxAsync`; public stateless cursors do not pin it. Quotas stop producers before partial publication. Projection batches have a bounded progress reserve so a full producer outbox can still be processed and reclaimed: one reserve-using batch per consumer until the retained prefix advances. Ordinary producers cannot spend that reserve. Cleanup is explicit; automatic cleanup remains part of governor qualification. See the [change-feed and outbox contract](docs/design/change-feeds.md).

## Backups and Cartograph

The canonical backup includes the checksummed redo journal, database identity and a SHA-256 manifest. Domain data, schemas, credentials, outcomes and inbox receipts are journaled together. The journal can begin with a verified checkpoint followed by newer transaction frames. ZoneTree files can be rebuilt from that canonical history. Restore validates every manifest file, creates a new incarnation, resets consensus routing metadata and leaves queue dispatch paused.

The current atomic-WAL source uses generated Orleans binary mutation payloads with the native raw-byte memory codec, explicit Put/Delete kinds, frame3 and identity4; native ZoneTree raw bytes, disk-flush ordering and checkpoint2 remain. Upgrading JSON/frame1 or prior unqualified binary/frame2 journals requires stopping all RF3 writers, Compact with the matching previous binary, and a verified compacted backup before upgrading every node. A remaining full legacy journal header is refused. [ADR-057](docs/ADR/ADR-057-orleans-atomic-wal.md) defines this offline transition; final GitHub fault qualification and measured acceleration are pending.

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
| `KeyLoad.Core` | Catalog, atomic mutation compilation, documents, outbox/change feeds, streams, queues, graph and samples |
| `KeyLoad.Storage.ZoneTree` | File ownership, redo recovery, consistent apply gate and verified backups |
| `KeyLoad.Replication` | Durable Raft append barrier, state-machine apply and bounded writer coordination |
| `KeyLoad.Orleans` | Physical-shard command facade and consensus-backed membership |
| `KeyLoad.Security` | Scope, row and sensitive-field policies |
| `KeyLoad.Query` | Shared SQL/JSON/C# queries, scalar live queries, exact vectors, BM25 and fusion |
| `KeyLoad.Server` | HTTP API, authenticated peer transport and bootstrap |
| `KeyLoad.Client` / `KeyLoad.Cli` | .NET SDK and administrative commands |
| `KeyLoad.Artifacts` | Optional Cartograph archive and ManagedCode storage transport |
| `KeyLoad.AppHost` / `KeyLoad.ServiceDefaults` | RF3 Aspire topology, health and OpenTelemetry |
| `KeyLoad.Analyzers` / `KeyLoad.Analyzers.Tests` | Source-owned Roslyn rules and real compiler regressions in the CodeQuality slice |
| `tests` / `benchmarks` | Unit/property, crash recovery, real RF3 integration and benchmark workloads |

## Verification

```sh
dotnet restore KeyLoad.slnx
dotnet build KeyLoad.slnx --no-restore --configuration Release
dotnet format KeyLoad.slnx --verify-no-changes --no-restore
gh workflow run ci.yml --repo managedcode/KeyLoad --ref main
gh run view <run-id> --repo managedcode/KeyLoad
```

Tests use TUnit and Microsoft.Testing.Platform and execute in GitHub Actions. Development builds and static checks are local evidence; qualification belongs to the workflow's exact SHA. Recovery qualification runs 1000 seeded real-process kills, checks complete-frame corruption and verifies a clean backup restore. Additional process kills cover checkpoint publication, native Raft append acknowledgement, subscription effects/inbox/checkpoint publication and projection effects/outbox/checkpoint publication. Integration tests own the Aspire lifecycle and use three independent server processes with isolated persistent directories. They kill the elected leader, retry the same command, verify atomic effects, live-query continuation and topic delivery on surviving voters, reject a minority write and erase/restart one replica to require snapshot catch-up of documents, outbox entries, generation checkpoints and inbox outcomes. No manually running AppHost is needed for tests.

The root `.editorconfig` is copied directly from Prostir. Every project enables SDK/static/style analysis with warnings as errors. Edit custom rules under `src/KeyLoad.Analyzers/Features/CodeQuality/` and add real-compilation cases under `tests/KeyLoad.Analyzers.Tests/Features/CodeQuality/`. They attach centrally to consumer projects and report in the IDE and build. Compiler SARIF 2.1 reports live under `artifacts/code-quality/<project>/<configuration>/<framework>/diagnostics.sarif`; CI retains them even when the build fails. See [CodeQuality](docs/Features/CodeQuality.md) for the rule catalog, authoring and applicability, and [current evidence](docs/implementation/code-quality.md) for actual gates.

The [runtime ledger](docs/implementation/runtime-qualification-20261002.md) preserves exact GitHub source SHAs, native suite counts, failures and artifact hashes; [AdminDashboard](docs/Features/AdminDashboard.md) records its separately verified RF3 and browser evidence. The latest joined 06a9 run has unit and image-preparation failures; RF3 and measured comparison stages did not run. Historical successful cases do not qualify newer source. Environmental failure branches, coverage, activation movement, server-resource/performance, endurance and power-loss qualification remain open.

The comparison library/sole CLI host split under [ADR-043](docs/ADR/ADR-043-comparison-library-host.md), immutable harness under [ADR-044](docs/ADR/ADR-044-benchmark-immutable-contracts.md), owned PostgreSQL schema repair under [ADR-045](docs/ADR/ADR-045-postgres-schema-ownership.md), private storage owners under [ADR-046](docs/ADR/ADR-046-storage-private-owners.md) and genuine BenchmarkDotNet library under [ADR-047](docs/ADR/ADR-047-embedded-benchmark-host.md) are source joins awaiting full runtime qualification. The read-only product contract migration preserves JSON/base64 and fingerprints under [ADR-041](docs/ADR/ADR-041-read-only-public-collections.md). Exact-SHA run [37015193756](https://github.com/managedcode/KeyLoad/actions/runs/37015193756) passed all88 analyzer cases and the enabled solution builds; subsequent repairs require renewed verification. Compatible coverage collection, container export and its numeric baseline remain pending. Historical source stages are retained in the [code-quality record](docs/implementation/code-quality.md); the [runtime ledger](docs/implementation/runtime-qualification-20261002.md) records the latest completed candidate run.

Memory and read-work repairs remain in progress across storage, SQL/search, events, time series, graph, messaging, replication and transport. The [repair inventory](docs/implementation/memory-performance.md) distinguishes source changes from proof. [Candidate CI37015193756](https://github.com/managedcode/KeyLoad/actions/runs/37015193756) remains failed and qualifies no system-wide performance gain. Portable vector scoring is present; its five finite/golden/real-store edge cases pass on three OSes. Validation optimization, software-fallback execution and matched RF3 server-resource/latency measurements remain open.

All test qualification and load measurements run in GitHub Actions. The comparison harness uses deterministic JSON, float32 vectors and cyclic graphs, verifies the complete returned payload, and retains every measured attempt, including failures. Reports include useful throughput, p50/p95/p99, separate enqueue/receive/ACK timings and load-generator CPU/allocation/RSS. These resource metrics describe the client process. See the [comparison methodology](docs/implementation/comparative-benchmarks.md).

The [public benchmark lab](https://www.keyload.cloud/) displays verified CI reports with workload, scenario, measure and repetition controls. CI runs a correctness smoke and two measured profiles with 1 KiB/16 KiB documents, eight/four clients and three/five graph hops. GitHub Pages publishes their reports only after the complete CI workflow succeeds; [website operations](docs/implementation/website.md) describes provenance and the custom domain. Current external baselines are single-node and contracts differ; matched-durability, database resource budgets, Marten/Wolverine and scaling qualification remain planned. These development observations do not establish an equal-durability winner or production readiness.

The separate [TimeSeries profile](docs/ADR/ADR-050-timeseries-timescale-comparison.md) ran a digest-pinned ephemeral TimescaleDB2.30.2-pg18 container, RF3 KeyLoad and published ManagedCode.TimeSeries10.0.0 in-memory aggregation. Its retained report atfa80c701 records20 successful attempts and20 matching correctness checks across48 identical samples, including range/boundary/offset/empty/invalid handling, buckets and cleanup. The complete Aspire test failed its native runner-completion gate and did not reach the foreign-schema assertion; it remains unqualified. The library has no persistence guarantee and the Timescale container has no cross-run data volume. [Exact receipts](docs/implementation/runtime-qualification-20261002.md) keep those guarantees and partial evidence explicit.

The product website redesign is in progress under [ADR-040](docs/ADR/ADR-040-static-site-threejs-evidence.md): a product introduction, a conceptual Three.js RF3 illustration and the complete evidence workspace. Its independent TUnit site suite runs in GitHub Actions against authentic historical reports. Website source, measured source and raw hashes stay distinct; the preview does not qualify current database changes or publish the pending nine-engine comparison profiles.

The next comparison contract adds MongoDB, OpenSearch and KurrentDB, expected-no-stream append/read, and real native replicated groups. [BenchmarkComparisons](docs/Features/BenchmarkComparisons.md) records requirements and acceptance; [ADR-034](docs/ADR/ADR-034-cluster-comparisons.md) records the exact support, topology, acknowledgement and publication contracts. It is not yet a qualified nine-engine result. KurrentDB is pinned to 26.1.2 to retain free clustering; Neo4j Community clustering is unavailable without Enterprise.

## Repository workflow

This repository follows the [MCAF tutorial](https://mcaf.managed-code.com/tutorial). The root [AGENTS.md](AGENTS.md) is merged with the original mandatory policy, and every project/module has its own local policy. Non-trivial work starts with stable requirement and acceptance IDs plus ADR implementation contracts; bounded workers own disjoint files, and integration joins their reviewed evidence before qualification. This MCAF setup installs no skills. See [RepositoryGovernance](docs/Features/RepositoryGovernance.md) and the [installation record](docs/implementation/mcaf-installation.json).

## Dependencies and license

The baseline uses .NET 10, Orleans 10.3.1, Aspire 13.6.0, ZoneTree 1.9.8, .NEXT 6.8.1 and appropriate ManagedCode libraries. See [Directory.Packages.props](Directory.Packages.props) and the [dependency survey](docs/implementation/dependency-survey.json). `ManagedCode.Orleans.Graph` restricts grain call relationships; the property graph lives in KeyLoad's canonical data model.

KeyLoad is released under the [MIT license](LICENSE). Dependency licenses remain with their respective owners. Contribution checks and dependency ownership rules are described in [AGENTS.md](AGENTS.md).

The owner-directed comparison workflow now targets Linux and plans a separate
runner for every native engine ×1/2/3 nodes ×scenario. [ADR-056](docs/ADR/ADR-056-isolated-linux-comparison-cells.md)
freezes the common intensive workload, exact mutation correctness, per-worker
JSON and complete authenticated aggregation before site generation. The initial
270-cell plan includes read/create/update/delete and specialized scenarios;
unsupported Community topology has an explicit reason and no measurement.
Source integration is in progress. No new intensive cohort or performance claim
is qualified yet; production default remains RF3.
