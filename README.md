<p align="center">
  <img src="site/favicon.svg" width="88" height="88" alt="KeyLoad, the AI-native database for AI agents">
</p>

<h1 align="center">KeyLoad</h1>

<p align="center">
  <strong>The AI-native database. One database for AI agents.</strong><br>
  Documents, typed tables, graphs, vectors/search, blobs, queues, events and time series in one replicated .NET cluster.<br>
  Everything links to everything else, and agents reach all of it through SQL, a built-in MCP server and a typed .NET SDK.
</p>

<p align="center">
  <a href="https://github.com/managedcode/KeyLoad/actions/workflows/build-and-tests.yml"><img src="https://github.com/managedcode/KeyLoad/actions/workflows/build-and-tests.yml/badge.svg" alt="Build and Tests"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Elastic%202.0-black" alt="Elastic License 2.0"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/built%20on-Orleans-0b5cad" alt="Built on Orleans">
  <img src="https://img.shields.io/badge/storage-ZoneTree-222222" alt="ZoneTree storage">
  <img src="https://img.shields.io/badge/MCP-server%20built%20in-6e56cf" alt="MCP server built in">
  <img src="https://img.shields.io/badge/status-development%20preview-orange" alt="Development preview">
</p>

<p align="center">
  <a href="https://www.keyload.cloud/">Website</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#capability-matrix">Capabilities</a> ·
  <a href="#why-net-orleans-and-zonetree">Why .NET, Orleans and ZoneTree</a> ·
  <a href="docs/README.md">Docs</a> ·
  <a href="#project-status">Status</a>
</p>

---

## Why an AI-native database?

**Agents shouldn't need a dozen databases.** Records, embeddings, tasks, files, events and relationships often mean separate systems, credentials and backups. KeyLoad puts that context and those actions in one database for AI agents.

```mermaid
flowchart TB
    subgraph Today["Today: one agent, seven systems"]
        direction TB
        A1["AI agent"] --> PG[("Postgres<br/>records")]
        A1 --> VDB[("Vector DB<br/>embeddings")]
        A1 --> MQ[("Redis / RabbitMQ<br/>tasks")]
        A1 --> S3[("S3<br/>files")]
        A1 --> KF[("Kafka<br/>events")]
        A1 --> GDB[("Graph DB<br/>relationships")]
        A1 --> TSDB[("Time-series DB<br/>metrics")]
    end
    subgraph One["With KeyLoad: one database"]
        direction TB
        A2["AI agent"] --> KL[("KeyLoad<br/>every model, one reference,<br/>one permission check")]
    end
    Today ~~~ One
```

| Built around agents | What that means for you |
|---|---|
| **Linked models** | Documents, typed tables, graphs, blobs, queues, events, vectors/search and time series share canonical entity references |
| **Shared operations** | SQL is the familiar shared language; MCP and the typed .NET SDK reach the same operations |
| **Persisted permissions** | Database-owned API keys and row/field policies; callers cannot grant themselves roles |
| **Atomic batches** | A document, event, graph edge and queued task in one partition commit together |
| **Bounded work** | Explicit full-scan permission and caps on work and memory |
| **Three replicas** | An Orleans RF3 cluster with node-local ZoneTree storage |

### Why we think this is the future

A support agent needs a ticket, customer history, similar cases, relationships and a follow-up task. Those belong together. Our goal is **one authorized operation** that builds context and records the next action.

Same-partition writes already commit atomically; leased receives, blob uploads and cross-partition work are separate steps today. [Try the development preview](#quick-start), [check its status](#project-status) and [share your workload](https://github.com/managedcode/KeyLoad/issues).

## What you can build

| You want to… | KeyLoad gives you |
|---|---|
| Give an agent long-term **memory and retrieval (RAG)** | Documents, files and embeddings in one place, with vector, full-text and hybrid search |
| Build a **knowledge graph** | Relationships between any entities, including typed rows, and graph traversal |
| Run **reliable agent workflows** | Queues with scheduling, leases, retries and dead letters, stored next to the data they change |
| Make **event-driven** apps | Ordered event history, durable subscriptions, change feeds and projections |
| Analyze **time series** | Timestamped samples, range reads, aggregates, time windows and retention |
| Store **files** for agents | Chunked uploads and partial reads of large blobs |

## One database, connected models

### Capability matrix

| Model | In one atomic commit | Read and query | SQL | MCP tools |
|---|---|---|---|---|
| 📄 Documents | ✅ Put, patch, delete with revision checks | Get by key, indexed queries, live queries | `SELECT` | `keyload_documents_*`, `keyload_query_*` |
| 🧾 Typed tables | ✅ Rows with a schema, primary keys and unique constraints | Indexed queries | `SELECT` | `keyload_documents_commit`, `keyload_sql_execute` |
| 🕸️ Graphs | ✅ Add and remove edges | Graph traversal | `CALL` | `keyload_graph_traverse` |
| 🧭 Vectors and search | ✅ Store a vector with its document | Exact vector, full-text and hybrid search | `CALL` | `keyload_search_execute` |
| 📬 Queues and topics | ✅ Enqueue, publish | Receive with leases, ACK, retries, dead letters, peek | `SELECT … FROM QUEUE_MESSAGES(…)` | `keyload_messages_*`, `keyload_subscriptions_*` for topics |
| 📜 Events | ✅ Append with an expected revision | Read, replay, durable subscriptions | `SELECT … FROM EVENTS(…)` | `keyload_streams_*`, `keyload_subscriptions_*` |
| 📈 Time series | ✅ Append samples | Ranges, latest value, aggregates, windows, retention | `CALL` | `keyload_series_*` |
| 📦 Files and blobs | Separate upload and publish steps | Chunked uploads, byte-range reads | `CALL` | `keyload_blobs_*` |
| 🔁 Change feeds | Document changes recorded in the same commit | Resumable change feed, projections | `CALL` | `keyload_changes_read`, `keyload_projections_*` |

`SELECT` reads data. `CALL` runs any operation from the same catalog the MCP server exposes, so every model is reachable from SQL.

### Models that work together

| Flow | What one request does |
|---|---|
| **Queue to knowledge graph** (`QueueToGraph`) | Reads ready messages, resolves linked entities and writes knowledge-graph relationships |
| **Graph to queued actions** (`GraphToQueueMutation`) | Follows relationships and can enqueue actions for linked entities |

```mermaid
flowchart LR
    Q[["Queue: inbox"]] -->|"QueueToGraph"| E["Linked entities<br/>documents and rows"]
    E --> G(("Knowledge graph"))
    G -->|"GraphToQueueMutation"| A[["Queue: actions"]]
```

The current composition API uses .NET `CommitAsync`, SQL `CALL keyload_documents_commit(@arguments)` or the official MCP server.

| Boundary | Current contract |
|---|---|
| Atomicity | The same atomic partition and transaction domain (`PartitionRef`); the batch succeeds or rolls back together |
| Queue reads | Composition does not lease or ACK messages |
| Files | Blobs remain part of the same database, with separate upload and publication operations |
| Preview limits | Full declarative SQL, the native SQL-client protocol and cross-partition composition are still in development; production guarantees remain under qualification |

Details: [composition guide](docs/Features/DatabaseComposition.md) · [transaction contract](docs/ADR/ADR-067-composable-agent-database.md).

## How it works

```mermaid
flowchart LR
    subgraph Callers
        SDK[".NET SDK"]
        MCP["AI agent over MCP"]
        SQL["SQL over HTTP"]
        CLI["CLI and admin console"]
    end
    subgraph Cluster["KeyLoad cluster (3 nodes)"]
        REQ["Request grain<br/>one per request"]
        PART["Partition grains"]
        N1[("Node 1<br/>ZoneTree")]
        N2[("Node 2<br/>ZoneTree")]
        N3[("Node 3<br/>ZoneTree")]
    end
    SDK --> REQ
    MCP --> REQ
    SQL --> REQ
    CLI --> REQ
    REQ --> PART
    PART --> N1
    PART --> N2
    PART --> N3
```

Each authenticated call gets its own Orleans request grain. Partition grains route to node-local storage owners; moving a grain does not move its storage handles. Writes require a persisted majority of the three replicas. See the [architecture map](docs/Architecture.md).

## Why .NET, Orleans and ZoneTree

| Foundation | Why we use it |
|---|---|
| [.NET 10](https://github.com/dotnet/runtime) | C# end to end, pooled buffers, `Span<T>` and native SIMD intrinsics; speedups need measurements |
| [Orleans](https://github.com/dotnet/orleans) | Request isolation, membership, routing and generated binary serialization; experimental directory/repartitioning remain under qualification |
| [ZoneTree](https://github.com/ZoneTree/ZoneTree) | Embedded ordered storage and a write-ahead log for every model; data stays on its node |
| [ZoneTree.FullTextSearch](https://github.com/ZoneTree/ZoneTree.FullTextSearch) | Native full-text indexing on the same storage foundation |
| [Aspire](https://github.com/dotnet/aspire) | Owns Docker cluster startup, readiness and cleanup |

**Orleans moves the routing; ZoneTree keeps the data in place.**

## Quick start

**You need:** the .NET SDK version pinned in [global.json](global.json), and Docker.

**1. Build the solution**

```bash
dotnet restore KeyLoad.slnx
```

```bash
dotnet build KeyLoad.slnx --no-restore --configuration Release
```

**2. Build the server image**

The cluster runs the server image pinned by its digest, so build it from source and push it to a local registry:

```bash
docker run -d --name keyload-registry -p 5050:5000 registry:3
```

```bash
docker build -t localhost:5050/keyload/server:dev . && docker push localhost:5050/keyload/server:dev
```

```bash
export KeyLoad__ContainerImages__Server="localhost:5050/keyload/server:dev@$(docker inspect --format '{{index .RepoDigests 0}}' localhost:5050/keyload/server:dev | cut -d@ -f2)"
```

**3. Start the three-node cluster**

```bash
dotnet run --project src/KeyLoad.AppHost --configuration Release --no-build
```

[Aspire](https://github.com/dotnet/aspire) starts three nodes at `http://localhost:5101`, `:5102` and `:5103`. Node data and your local development credentials are stored in `data/cluster/`, which git ignores. Keep that folder between restarts.

**4. Look around**

- Open **http://localhost:5101/admin** for the admin console.
- Or check the cluster from the CLI:

```bash
dotnet run --project src/KeyLoad.Cli --configuration Release --no-build -- status http://localhost:5101 data/cluster/local-profile.json
```

Your local admin API key is the `AdminKey` value in `data/cluster/local-profile.json`. Export it as `KEYLOAD_API_KEY` for the examples below.

## Use it

### From .NET

The [client SDK](src/KeyLoad.Client) gives you typed database operations. This creates a collection, writes an order and reads it back:

```csharp
using KeyLoad;
using KeyLoad.Client;

var apiKey = Environment.GetEnvironmentVariable("KEYLOAD_API_KEY")
    ?? throw new InvalidOperationException("Set KEYLOAD_API_KEY.");
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5101") };
var client = new KeyLoadClient(http, apiKey);

// tenant, database, transaction domain, partition key
var partition = new PartitionRef("acme", "shop", "orders", "customer-42");

var collection = await client.ConfigureResourceAsync(Guid.NewGuid(), new("acme", "shop",
    new ResourceDefinition("orders", ResourceKind.Collection, "orders")));
collection.ThrowIfFail();

var commandId = Guid.NewGuid(); // Reuse this ID if you retry the same write.
var result = await client.CommitAsync(new CommandRequest(commandId, partition,
[
    new PutDocument("orders", "order-1", "{\"status\":\"new\"}", ExpectedRevision: 0)
]));
result.ThrowIfFail();

var order = await client.GetAsync(new EntityRef(partition, "orders", "order-1"));
order.ThrowIfFail();
```

### With SQL

Use SQL to read data and to call any database operation:

```csharp
using System.Text.Json;

var rows = await client.ExecuteSqlAsync(new SqlOperationRequest(partition,
    "SELECT * FROM orders WHERE status = @status LIMIT 20",
    new() { ["status"] = JsonSerializer.SerializeToElement("new") },
    AllowFullScan: true));
rows.ThrowIfFail();
Console.WriteLine(rows.Value);
```

Here's what works today:

```sql
SELECT * FROM orders WHERE number BETWEEN 1 AND 9 ORDER BY id  -- filter and sort documents
SELECT * FROM QUEUE_MESSAGES('jobs')                           -- peek at a queue without consuming it
SELECT e.eventType FROM EVENTS('events', 'stream-a', 3) AS e   -- read event history
EXPLAIN SELECT * FROM orders                                   -- see the query plan
CALL keyload_documents_commit(@arguments)                      -- run any database operation
```

Model views (`QUEUE_MESSAGES`, `EVENTS`) and unindexed filters require `AllowFullScan: true`, and model views don't support cursors. Full SQL, including joins, is still in progress. The [query guide](docs/Features/QueryExecution.md) lists the supported syntax, and the [compatibility inventory](docs/implementation/sql-client-conformance.json) tracks the rest.

### From an AI agent (MCP)

Every node serves [MCP](https://modelcontextprotocol.io/) at `/mcp`. Its compact catalog offers `gateway_tools_search`, `gateway_tools_route` and `gateway_tool_invoke`; tools are discovered on demand. Connect with an API key, for example in Claude Code's `.mcp.json`:

```json
{
  "mcpServers": {
    "keyload": {
      "type": "http",
      "url": "http://localhost:5101/mcp",
      "headers": { "Authorization": "Bearer ${KEYLOAD_API_KEY}" }
    }
  }
}
```

| Step | Agent workflow |
|---|---|
| **Learn** | Read `keyload://guides/agent-quickstart` or request the no-argument `keyload_agent_quickstart` prompt |
| **Discover** | Call `gateway_tools_search` with `{ "query": "keyload_query_capabilities", "maxResults": 1 }` |
| **Inspect** | Use the returned exact schema and effect hints |
| **Invoke** | Call `gateway_tool_invoke` with `{ "toolId": "keyload_query_capabilities", "arguments": {} }` |

Every invocation checks current persisted permissions. Retry uncertain writes with the same command identity and payload. Details: [API guide](docs/Features/ClientApi.md) · [discovery and qualification contract](docs/Features/ClientApi/ToolDiscovery.md).

## Project status

> **Development preview.** Ready to explore; production qualification is still open.

| Original plan | Accepted | In progress | Pending |
|---|---|---|---|
| 104 tasks | **17** | **87** | **0** |

```mermaid
flowchart TB
    Core["1 · Core slices<br/>17 original tasks accepted"]
    Models["2 · Connected workflows<br/>Implementation in progress"]
    Checks["3 · Qualification<br/>Faults, coverage and performance"]
    Release["4 · Production release<br/>Gated on qualification"]
    Core --> Models --> Checks --> Release
    classDef accepted fill:#eee8f5,stroke:#6e56cf,color:#171717
    classDef active fill:#f7eef3,stroke:#97718d,color:#171717
    classDef gated fill:#f2f2f2,stroke:#777,color:#171717
    class Core accepted
    class Models,Checks active
    class Release gated
```

**Accepted slice** means its original task criteria passed for the recorded source. **In source** means implemented with qualification still open. Later changes need fresh checks; these stages are not a release schedule.

| Workstream | What it delivers | Why it matters | State | What remains |
|---|---|---|---|---|
| **Storage & documents** | ZoneTree, CRUD, revision checks, strict indexes | Keep records consistent | ✓ Accepted slices | [Complete recovery and fault gates](docs/Features/StorageRecovery.md) |
| **Backup & restore** | Local database backup and restore | Recover a saved database | ✓ Accepted local slice | [RF3 cluster restore drills](docs/Features/BackupRestore.md) |
| **Atomic batches** | Same-partition writes and persisted retries | Commit related changes together | ✓ Accepted slices | [Cross-partition composition](docs/Features/DatabaseComposition.md) |
| **Graphs** | Stored edges and bounded traversal | Connect agent knowledge | ✓ Accepted slices | [Cross-partition graph work](docs/Features/GraphTraversal.md) |
| **Time series** | Ordered samples, ranges, aggregates and retention | Track history and trends | ✓ Accepted slices | [Chunk and scale qualification](docs/Features/TimeSeries.md) |
| **Search** | Exact vectors, hybrid ranking and Explain | Retrieve useful context | ✓ Accepted slices | [ANN, text rebuild and freshness](docs/Features/Search.md) |
| **SQL & typed tables** | `SELECT`, model views, `CALL`, bounded INNER JOIN | Use one familiar language | ◐ In source | [Full SQL, foreign keys and native client protocol](docs/Features/QueryExecution.md) |
| **Queues, events & composition** | Leases, event history and queue ↔ graph flows | Turn knowledge into actions | ◐ In source | [Complete failure, security and recovery flows](docs/Features/Messaging.md) |
| **SDK, MCP & tools** | .NET SDK, HTTP, MCP discovery, CLI and admin | Connect applications and agents | ◐ In source | [Current gateway and official MCP RF3 checks](docs/Features/ClientApi/ToolDiscovery.md) |
| **Authorization** | Persisted keys and row/field policies | Keep private context private | ◐ In source | [Complete adversarial and revocation checks](docs/Features/Authorization.md) |
| **RF3 & Orleans** | Three replicas, follower reads and partition movement | Keep data through node failures | ◐ In source | [Current-source faults, restart and runtime checks](docs/Features/ClusterRouting.md) |
| **RAM, SIMD & performance** | Bounded caches and vectorized CPU work | Reduce latency and memory use | ◐ In progress | [Comparable Linux measurements](docs/implementation/memory-performance.md) |
| **Production release** | Qualified database and operator guidance | Trust important data | ○ Gated | Full unit/scalar, recovery, RF3, coverage, endurance and power-loss gates |

Complete product functional coverage is **unmeasured**. Performance figures require original GitHub measurements; process-kill tests do not prove power-loss durability.

[Task-by-task status and original results](docs/implementation/status.json) · [SQL compatibility inventory](docs/implementation/sql-client-conformance.json) · [Coverage requirements](docs/Features/CodeQuality.md) · [Benchmark methodology](docs/Features/BenchmarkComparisons/Methodology.md) · [Published benchmarks](https://www.keyload.cloud/#benchmarks)

## FAQ

| Question | Answer |
|---|---|
| **Is this a vector database?** | Vectors share a database with documents, graphs, queues and files. Exact search is available; [ANN is in progress](docs/Features/Search/ManagedAnn.md). |
| **Can it hold agent memory?** | Yes: linked documents, embeddings, files, graphs and history are the main use case. |
| **Does it replace a stack of databases?** | That's the goal for agent workloads. Check the [preview limits](#project-status) before moving data. |
| **How do agents connect?** | MCP at `/mcp`, with an API key and on-demand tool discovery. |
| **What about Python or TypeScript?** | Use MCP or `/v1/` HTTP. The typed SDK is .NET. |
| **Can I use it commercially?** | [Elastic License 2.0](LICENSE) permits application use. Offering a substantial set of KeyLoad features as a hosted or managed service requires ManagedCode authorization. KeyLoad is source available. |
| **Production ready?** | Not yet: endurance, fault and power-loss qualification remain open. |

## Repository map

Feature code uses `Features/<SliceName>/<Responsibility>/` across the solution.

| Path | What's inside |
|---|---|
| [`src/KeyLoad.Server`](src/KeyLoad.Server) | The database server: HTTP API, MCP server and admin console |
| [`src/KeyLoad.Client`](src/KeyLoad.Client) | The .NET SDK |
| [`src/KeyLoad.Cli`](src/KeyLoad.Cli) | Command-line tool for operators and workers |
| [`src/KeyLoad.AppHost`](src/KeyLoad.AppHost) | Aspire host for the local cluster and every test suite |
| [`src/KeyLoad.Abstractions`](src/KeyLoad.Abstractions) | Shared public contracts |
| `src/KeyLoad.Core`, `Query`, `Orleans`, `Replication`, `Security`, `Storage.*` | Database engine, SQL, cluster, replication, permissions and storage |
| [`tests/`](tests) | Unit, crash-recovery, three-node and website tests ([TUnit](https://github.com/thomhurst/TUnit)) |
| [`benchmarks/`](benchmarks) | Comparisons with other databases, plus microbenchmarks |
| [`site/`](site) | Source for [keyload.cloud](https://www.keyload.cloud/) |
| [`docs/`](docs/README.md) | Architecture, feature specifications and design decisions |

## Contributing

Start with the [architecture map](docs/Architecture.md), the [feature specs](docs/Features) and [repository rules](AGENTS.md). After building, run a functional suite (`unit`, `unit-scalar`, `recovery`, `rf3`, `analyzers` or `site`):

```bash
node scripts/Features/TestInfrastructure/run-tests.mjs --KeyLoadTests:Suite=unit
```

```bash
dotnet format KeyLoad.slnx --verify-no-changes --no-restore
```

Native TUnit fixtures own Aspire startup and cleanup; `rf3` uses real .NET and official MCP clients against three Docker nodes. Benchmark builds, checks and measurements run exclusively in GitHub's Benchmarks workflow.

## Credits

KeyLoad is developed by [Managed Code](https://www.managed-code.com/) and stands on the shoulders of these projects. Thank you to their authors and contributors.

| Project | How KeyLoad uses it |
|---|---|
| [.NET](https://github.com/dotnet/runtime) and [ASP.NET Core](https://github.com/dotnet/aspnetcore) | Server, client SDK and HTTP hosting |
| [Orleans](https://github.com/dotnet/orleans) | Distributed request execution, cluster routing and binary serialization |
| [ZoneTree](https://github.com/ZoneTree/ZoneTree) | Persistent ordered storage for every database model |
| [ZoneTree.FullTextSearch](https://github.com/ZoneTree/ZoneTree.FullTextSearch) | Full-text search index |
| [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) | Built-in MCP server and real MCP clients in tests |
| [ManagedCode.Communication](https://github.com/managedcode/Communication) | Typed operation results and ASP.NET Core/Orleans integration |
| [ManagedCode.Storage](https://github.com/managedcode/Storage) | File-system storage and backup transfer support |
| [ManagedCode.TimeSeries](https://github.com/managedcode/TimeSeries) | Time-series aggregation |
| [ManagedCode.Orleans.Graph](https://github.com/managedcode/Orleans.Graph) | Grain call relationship policies |
| [ManagedCode.Orleans.Identity](https://github.com/managedcode/Orleans.Identity) | Native Orleans request identity context |
| [ManagedCode.MCPGateway](https://github.com/managedcode/MCPGateway) | Agent authentication and on-demand tool discovery |
| [ManagedCode.MarkdownLd.Kb](https://github.com/managedcode/markdown-ld-kb) | Knowledge-graph search over tool metadata |
| [Cartograph](https://github.com/angelhernandezm/Cartograph) | Segmented backup archives and catalogs |
| [OpenTelemetry .NET](https://github.com/open-telemetry/opentelemetry-dotnet) | Logs, metrics and traces |

| Development & presentation | Contribution |
|---|---|
| [Aspire](https://github.com/dotnet/aspire) · [TUnit](https://github.com/thomhurst/TUnit) | Docker orchestration and real operation tests |
| [Roslyn](https://github.com/dotnet/roslyn) · [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) | Repository analyzers and microbenchmarks |
| [Three.js](https://github.com/mrdoob/three.js) | Website cluster illustration |

The comparison suite uses free community editions and real native clients:

| Workload family | Comparison projects | Client projects |
|---|---|---|
| Records & SQL | [PostgreSQL](https://github.com/postgres/postgres), [MongoDB](https://github.com/mongodb/mongo), [Redis](https://github.com/redis/redis) | [Npgsql](https://github.com/npgsql/npgsql), [MongoDB .NET Driver](https://github.com/mongodb/mongo-csharp-driver), [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) |
| Vectors & text | [pgvector](https://github.com/pgvector/pgvector), [Qdrant](https://github.com/qdrant/qdrant), [OpenSearch](https://github.com/opensearch-project/OpenSearch) | — |
| Queues & events | [RabbitMQ](https://github.com/rabbitmq/rabbitmq-server), [KurrentDB](https://github.com/kurrent-io/KurrentDB) | [RabbitMQ .NET Client](https://github.com/rabbitmq/rabbitmq-dotnet-client), [KurrentDB .NET Client](https://github.com/kurrent-io/KurrentDB-Client-Dotnet) |
| Graphs & connected models | [Neo4j](https://github.com/neo4j/neo4j), [SurrealDB](https://github.com/surrealdb/surrealdb), [HelixDB](https://github.com/helixdb/helix-db) | — |
| Time series | [TimescaleDB](https://github.com/timescale/timescaledb) | — |

Active scale: **100,000 and 1,000,000 records**, at native **1 or 3 nodes** where supported. New adapters remain under qualification. The [vector comparison contract](docs/Features/BenchmarkComparisons/VectorQualification.md) defines accuracy, latency, index cost and memory checks; only qualified original GitHub results become public figures.

## License

KeyLoad is licensed under the [Elastic License 2.0](LICENSE). Use it in your own applications; contact [ManagedCode](https://www.managed-code.com/) for authorization to offer it as a hosted or managed database service to third parties. Dependency licenses remain with their respective owners.

---

<p align="center">Developed by <a href="https://www.managed-code.com/">Managed Code</a></p>
