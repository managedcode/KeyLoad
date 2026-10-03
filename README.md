# KeyLoad

**One database for AI agents.**

KeyLoad brings documents, typed tables, graphs, blobs, queues, events, vectors/search and time series into one database. Models share canonical entity references: a task can point to a document, that document can be part of a knowledge graph, and its files, history and search results stay connected.

SQL is the familiar shared language for combining these models. Agents and applications connect through the .NET SDK, the integrated official MCP server, or the HTTP and simple agent APIs.

[Website](https://www.keyload.cloud/) · [Documentation](docs/README.md) · [Architecture](docs/Architecture.md)

## What you can build

- **Agent memory and retrieval:** keep source documents, files, embeddings and relationships together for contextual search.
- **Knowledge graphs:** connect documents and typed rows, traverse their relationships, and turn discoveries into tasks.
- **Reliable agent workflows:** schedule work, lease tasks, retry failures and record processing results alongside database changes.
- **Event-driven applications:** retain event history, follow changes and maintain projections.
- **Time-series analysis:** store timestamped samples and read ranges, aggregates and time windows.

| Data model | Purpose |
|---|---|
| Documents and typed tables | JSON records, schema-constrained rows, indexes and revision-checked updates |
| Graphs | Relationships between entities and bounded traversal |
| Vectors and text search | Exact vector retrieval, lexical search and combined ranking |
| Files and blobs | Chunked uploads and partial reads |
| Queues | Scheduled work, leases, retries and dead-letter handling |
| Events and topics | Ordered history, durable subscriptions and checkpoints |
| Time series | Timestamped samples, range reads and aggregation |

## One database, connected models

Collections, tables and queues organize data within the same database. An agent can follow references across models while the database checks its permissions.

- **Queue to knowledge graph.** `QueueToGraph` reads ready messages, resolves their linked entities and writes knowledge-graph relationships.
- **Graph to queued actions.** `GraphToQueueMutation` follows visible graph relationships to enqueue actions referencing those entities.

The current composition API submits these operations through .NET `CommitAsync`, SQL `CALL keyload_documents_commit(@arguments)` or official MCP. Resources must share the same atomic partition and transaction domain; the batch succeeds or rolls back together. Queue-to-graph projection does not lease or ACK messages. Blobs remain part of the same database, with separate upload and publication operations.

See the [composition guide](docs/Features/DatabaseComposition.md) and its [transaction contract](docs/ADR/ADR-067-composable-agent-database.md) for limits, permissions and retries.

## Development status

KeyLoad is a **development preview**. The current SQL subset provides bounded document queries and calls to database operations. Full declarative SQL, the native SQL-client protocol and cross-partition composition are still in development.

The default server is a three-node replicated cluster (RF3), built on Orleans with persistent ZoneTree storage on each node. Production readiness, endurance, power-loss durability and comparative performance remain under qualification. The [implementation tracker](docs/implementation/status.json), [SQL compatibility inventory](docs/implementation/sql-client-conformance.json) and [test results](docs/implementation/runtime-qualification-20261002.md) record the detailed status.

The server uses ZoneTree.FullTextSearch for bounded derived text candidates while preserving exact authorized ranking. Its [development receipt](docs/implementation/native-full-text-development-2026-10-03.json) records 26 native unit and 10 process-recovery checks through Aspire. Full Linux/RF3 qualification and incremental projection replay remain pending; these checks establish no speed improvement.

## Get started

Install the .NET SDK selected in [global.json](global.json) and Docker, then build from the repository root:

```sh
dotnet restore KeyLoad.slnx
dotnet build KeyLoad.slnx --no-restore --configuration Release
```

To run the RF3 cluster, configure `KeyLoad__ContainerImages__Server` with a source-built server image in `registry/image:tag@sha256:<digest>` form, then start the [Aspire AppHost](src/KeyLoad.AppHost):

```sh
dotnet run --project src/KeyLoad.AppHost --configuration Release --no-build
```

Aspire starts three Docker nodes at `http://localhost:5101`, `http://localhost:5102` and `http://localhost:5103`. Node data and the private development credentials live under `data/cluster/`; keep them across restarts. Container configuration is described in the [Docker setup](docs/Features/BenchmarkComparisons.md#digest-backed-docker-execution-continuation).

Open `http://localhost:5101/admin` for the administration console, or check the cluster with the CLI:

```sh
dotnet run --project src/KeyLoad.Cli --configuration Release --no-build -- \
  status http://localhost:5101 data/cluster/local-profile.json
```

## Use the .NET SDK

The [client SDK](src/KeyLoad.Client) exposes typed database operations. This example creates a collection and writes an order using an API key with the required permissions. Set `KEYLOAD_API_KEY` in your application environment; the local development key is stored in `data/cluster/local-profile.json` as `AdminKey`.

```csharp
using KeyLoad;
using KeyLoad.Client;

var apiKey = Environment.GetEnvironmentVariable("KEYLOAD_API_KEY")
    ?? throw new InvalidOperationException("Set KEYLOAD_API_KEY.");
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5101") };
var client = new KeyLoadClient(http, apiKey);
var partition = new PartitionRef("acme", "shop", "orders", "customer-42");

var collection = await client.ConfigureResourceAsync(Guid.NewGuid(), new("acme", "shop",
    new ResourceDefinition("orders", ResourceKind.Collection, "orders")));
collection.ThrowIfFail();

var commandId = Guid.NewGuid(); // Keep this ID when retrying the same write.
var result = await client.CommitAsync(new CommandRequest(commandId, partition,
[
    new PutDocument("orders", "order-1", "{\"status\":\"new\"}", ExpectedRevision: 0)
]));
result.ThrowIfFail();

var order = await client.GetAsync(new EntityRef(partition, "orders", "order-1"));
order.ThrowIfFail();
```

For agent integrations, the [MCP and API guide](docs/Features/ClientApi.md) describes the shared operation catalog. The [query guide](docs/Features/QueryExecution.md) covers supported SQL and query limits; the [documentation index](docs/README.md) links the remaining database APIs.

## Credits

KeyLoad is developed by [Managed Code](https://www.managed-code.com/) and builds on these projects. Thank you to their authors and contributors.

| Project | How KeyLoad uses it |
|---|---|
| [.NET](https://github.com/dotnet/runtime) and [ASP.NET Core](https://github.com/dotnet/aspnetcore) | Server, client SDK and HTTP hosting |
| [Orleans](https://github.com/dotnet/orleans) | Distributed request execution, cluster routing and binary serialization |
| [ZoneTree](https://github.com/ZoneTree/ZoneTree) | Persistent ordered storage for the database models |
| [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) | Integrated MCP server and real MCP clients |
| [ManagedCode.Communication](https://github.com/managedcode/Communication) | Typed operation results and ASP.NET Core/Orleans integration |
| [ManagedCode.Storage](https://github.com/managedcode/Storage) | File-system storage and backup transfer support |
| [ManagedCode.TimeSeries](https://github.com/managedcode/TimeSeries) | Time-series aggregation |
| [ManagedCode.Orleans.Graph](https://github.com/managedcode/Orleans.Graph) | Grain call relationship policies |
| [Cartograph](https://github.com/angelhernandezm/Cartograph) | Segmented backup archives and catalogs |
| [OpenTelemetry .NET](https://github.com/open-telemetry/opentelemetry-dotnet) | Logs, metrics and traces |

Development and presentation also use [Aspire](https://github.com/dotnet/aspire) for Docker orchestration, [TUnit](https://github.com/thomhurst/TUnit) for tests, [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) for microbenchmarks, and [Three.js](https://github.com/mrdoob/three.js) for the website's cluster illustration.

## License

KeyLoad is licensed under [MIT](LICENSE).

Developed by [Managed Code](https://www.managed-code.com/).
