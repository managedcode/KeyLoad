# KeyLoad architecture and ownership map

Read the root and nearest project-local AGENTS.md before changing this solution.
The product specification is [product-specification.md](design/product-specification.md).
The [documentation index](README.md) lists the canonical Feature specifications;
each owning Feature defines stable REQ/AC, callers, boundaries, flows, tests and a
Mermaid diagram. The [ADR catalog](ADR/README.md) records decisions with their
implementation contracts. This document is a navigation map, not a replacement
specification or a readiness claim: the diagrams show ownership and contracts,
not proof that every target is implemented or qualified.

## Product

KeyLoad is **one database for AI agents**. Documents, typed tables, graphs, blobs,
queues, events, vectors/search and time series coexist in one database and
reference one another through canonical identities. Logical tables, collections
and queues organize that one database; they are not isolated model silos.

SQL is the familiar shared language for combining those models in one bounded
request. T-SQL syntax is parsed with Microsoft ScriptDOM and lowered into closed
typed operations ([ADR-130](ADR/ADR-130-microsoft-scriptdom-tsql.md)); SQL, the
.NET SDK and MCP all reach the same authorized bounded database operations
([ADR-054](ADR/ADR-054-central-sql.md), [ADR-065](ADR/ADR-065-full-sql-client-compatibility.md),
[versioned conformance inventory](implementation/sql-client-conformance.json)).
The motivating flows are queue messages -> referenced entities -> knowledge graph,
and graph relationships -> queued actions, owned by
[DatabaseComposition](Features/DatabaseComposition.md) and
[ADR-067](ADR/ADR-067-composable-agent-database.md). Typed rows reuse collection
entity storage so graph and vector references keep one identity
([RelationalStorage](Features/RelationalStorage.md),
[ADR-055](ADR/ADR-055-typed-relational-rows.md)).

```mermaid
flowchart LR
    Caller[Agent SQL SDK MCP] --> Connection[Orleans connection grain]
    Connection --> Tx[Authorized same partition ordered ZoneTree transaction]
    Tx --> Source[Ready queue links or visible graph traversal]
    Source --> Refs[Canonical document and typed row references]
    Refs --> Effects[Graph edges or queued actions]
    Effects --> Atomic[Primitive effects outbox and stable outcome]
```

```mermaid
classDiagram
    Mutation <|-- QueueToGraph
    Mutation <|-- GraphToQueueMutation
    QueueGraphLink --> EntityRef : canonical endpoints
    CommandRequest --> Mutation : ordered source and effect stages
    DatabaseEngine --> QueueGraphLink : authorized transaction expansion
    DatabaseEngine --> ReadExecutionBudget : operation cancellation and expiry
```

## Cluster and execution concept

Microsoft Orleans is the cluster
([ADR-136](ADR/ADR-136-orleans-partition-ownership.md),
[ADR-036](ADR/ADR-036-orleans-foundation.md)). Orleans does all of the
clustering: membership, silo discovery, failure detection, the distributed grain
directory, placement, activation migration and repartitioning, internal
communication (grain calls, Grain Service calls and Orleans streams), timers,
reminders, durable jobs and scaling. KeyLoad adds no clustering mechanism of its
own; scaling means adding silos to the Orleans cluster.

- **Connection grain.** Every connected client has one Orleans grain
  ([ADR-125](ADR/ADR-125-connection-owned-execution.md),
  [connection ownership](Features/ClientApi.md#persistent-connection-ownership)).
  Each operation carries its own verified identity, fresh persisted
  authorization, caller-supplied bounds and expiry, cancellation and idempotent
  command outcome. No node, tenant, principal or connection pool throttles
  callers ([ADR-145](ADR/ADR-145-no-request-throttling.md)), and no configured
  operation limit refuses work
  ([ADR-149](ADR/ADR-149-no-operation-limits.md)).
  The connection grain never caches trusted roles and never owns storage; it
  admits each operation and hands it to that operation's request grain.
- **Request grains.** One `RequestGrain` per admitted operation, keyed by its
  request identity and placed on the connection's silo, coordinates the
  operation: it resolves the storage owner, runs the work in place when its silo
  owns the data or through a `RequestExecutionGrain` on the owner's silo, fans
  partitioned queries, searches and composed maintenance out to the owners and
  gathers the results.
- **Partition grains.** A logical partition grain is an ordinary Orleans grain
  that serializes and routes the commands of one logical (atomic) partition.
- **Silo-local ZoneTree owner.** Each silo hosts a Grain Service; typed Orleans
  calls can address it on another silo. The physical owner retains ZoneTree,
  WAL/journals, locks and ordered apply. Local scoped services borrow that exact
  open session through DI. Isolated stores open during silo startup.
  Shared storage opens lazily for its ordinary database grain and closes after
  that activation's original work and borrowers join. Open handles never move
  with activations.
- **Copies.** The writer finds the target silos through Orleans
  membership (`IClusterMembershipService` / `IManagementGrain`) and sends the
  write through ordinary Orleans calls to the ZoneTree owner Grain Services on
  `KeyLoad:ReplicaCount` silos. The owner set and its canonical position are
  cached per Orleans membership version; silo cuts are re-read only after a
  membership change or a failed copy, not on every write. The write is
  acknowledged after every configured copy confirms persisted apply. `ReplicaCount` is positive, defaults
  to 1 and is not larger than `KeyLoad:SiloCount`; `KeyLoad:StorageMode` selects
  `IsolatedVolumes` (each silo owns its own disk, the default) or `SharedVolume`
  (one shared disk, `ReplicaCount=1`)
  ([ADR-143](ADR/ADR-143-independent-silo-replica-topology.md)). An unfinished original command can complete on
  a missing copy through a stable-ID retry or the commit owner's periodic
  `KeyLoad:SynchronizationInterval` timer. Both use the same ordered copy drain;
  unrelated gaps require explicit
  recovery and general empty-copy restoration remains unqualified.
- **Request flow.** Public API (HTTP, .NET SDK, MCP, SQL) -> connection grain ->
  one request grain per operation -> ZoneTree owner on the same node, or on other
  nodes through their request execution grains -> results are gathered -> returned
  to the caller.
- **Background work.** Queue deadlines, due and recurring work, remote
  transfers, ANN projection and time-series chunk work never check leadership.
  Orleans `[StatelessWorker(1)]` grains share one pool key per work kind, so each
  silo runs one polling activation. On an Orleans grain timer whose interval is a
  typed option (for example 5 seconds) the activation reads the next page of
  persisted due records from the storage owner and dispatches them through the
  normal grain -> ZoneTree owner path. Nothing is buffered in memory.
- **Identifiers.** Every grain key and every identifier for objects, counters
  and storage keys comes from one shared helper per identity kind. Keys are
  case-sensitive.

```mermaid
flowchart LR
    Caller[HTTP SDK MCP SQL caller] --> Api[Public API and ASP.NET Core authentication]
    Api --> Connection[Connection grain one per client]
    Connection --> Partition[Request grain one per operation]
    Partition --> Gather[Gather results from several partitions]
    Partition -.->|finds target silos| Membership[Orleans membership]
    Partition -->|local DI borrow| OwnerA[ZoneTree owner on silo A]
    Partition -->|local DI borrow| OwnerB[ZoneTree owner on silo B]
    Partition -->|local DI borrow| OwnerC[ZoneTree owner on silo C]
    Gather -->|Orleans calls| OwnerA
    Gather -->|Orleans calls| OwnerB
    OwnerA --> FilesA[(Local ZoneTree files)]
    OwnerB --> FilesB[(Local ZoneTree files)]
    OwnerC --> FilesC[(Local ZoneTree files)]
    Events[Background events] --> Worker[StatelessWorker grain buffer]
    Worker -->|Orleans timer batch| Partition
```

### Storage-mode contract and current qualification

Two storage modes are required. With a **shared volume**, all silos can reach the
configured partition paths without copying data. One ordinary Orleans grain owns
each open physical partition; the other silos call it. Orleans owns activation
placement and movement. The storage owner closes its actual handles before another
activation reopens that partition. The store's owner lock and `commands.wal` open with
exclusive file sharing, so several live writable instances over the same files are
unsupported.
The actual filesystem must preserve the required locking and flush semantics.

With **isolated volumes**, typed Orleans calls reach the owners of the required
logical partitions. The caller bounds parallel work and merges partial results.
A physical replica is another copy of one partition, not another partition to count
in the result. Stateless workers perform interchangeable Task-based work using
Orleans-managed activation pools.

The source implements lazy shared-session acquisition and joined close, isolated
full-copy routing, and ordinary partition grains for `PartitionQuery` and
`DistributedSearch`. A request pins one actual owner per distinct logical
partition and preserves its expiry, cancellation and read-cut checks across
all leaf stages. Full copies remain copies of the same database; this does not
implement a partition-to-disk sharding scheme. Both modes still require complete
runtime and exact-source Linux qualification, including the deployed shared
volume's locking and flush behavior.

Sources: [Orleans stateless workers](https://learn.microsoft.com/en-us/dotnet/orleans/grains/stateless-worker-grains),
[grain directory](https://learn.microsoft.com/en-us/dotnet/orleans/host/grain-directory),
and [the pinned ZoneTree WAL open contract](https://github.com/ZoneTree/ZoneTree/blob/a9953313613337f5ead6c3143e8da77b7103c64f/src/ZoneTree/WAL/Sync/SyncFileSystemWriteAheadLog.cs).

Credentials and authorization policies are persisted in the database; callers
never supply trusted roles. Standard ASP.NET Core authentication admits HTTP,
SDK and MCP callers, and ManagedCode.Orleans.Identity carries the verified
call-local identity between grains. OpenTelemetry logs, traces and metrics come
from the Orleans, ASP.NET Core and Aspire integrations without
credentials, payloads or unbounded labels. Orleans execution and
scheduling choices are mapped per grain and method in
[ExecutionPrimitives](Features/OrleansExecution/ExecutionPrimitives.md) and
[ADR-110](ADR/ADR-110-orleans-execution-primitives.md); the broader
[Orleans capability review](Features/OrleansExecution/CapabilityReview.md) covers
Streams, transactions, durable jobs and per-silo services. Orleans
transactional grain state does not by itself make ZoneTree writes atomic; it
needs a provider that participates in the canonical ZoneTree commit.

## Runtime projects and process entry

Runtime projects under `src/` have distinct consumers and responsibilities:
Abstractions (shared contracts), Client (external SDK), Cli (operator
executable), Core (database semantics and authorization evaluation), Query (SQL
compilation, search and change feeds), Clustering (Orleans grains, routing and
copies through calls to `ReplicaCount` silo-local ZoneTree owners),
Storage.ZoneTree (physical storage, local archive transfer and file IO),
Server (host composition, public API, health, service discovery, resilience and
OpenTelemetry) and Diagnostics (BCL-only bounded phase telemetry shared by
storage and replication). Diagnostics stays independent so low-level storage
does not depend on domain execution or the server host.

`src/` contains runtime product code only. Compiler rules live in `analyzers/`;
the Aspire AppHost and shared build targets live in `build/`; tests and
benchmarks keep their own roots. Recovery tests own their child-process
scenarios in the same test assembly. Compiler analyzers, the AppHost, benchmark
entry, generated-consumer fixtures and the TUnit comparison host keep their
distinct execution boundaries.

Each `Program.cs` shows initialization, mode selection, host composition, run and
joined shutdown; compiler rule KLD0020 rejects forwarding-only entry wrappers.
Runtime code reads time through an owned `TimeProvider`
([TimeProvider](Features/ResourceExecution/TimeProvider.md),
[ADR-115](ADR/ADR-115-time-provider.md)), operational policy through validated
typed options ([ADR-113](ADR/ADR-113-centralized-runtime-options.md)), runtime
identities through named constants ([ADR-111](ADR/ADR-111-magic-runtime-values.md))
and synchronization through Orleans turns, `System.Threading.Lock` or
`SemaphoreSlim.WaitAsync` ([ADR-108](ADR/ADR-108-typed-synchronization.md)).

## Complete repository boundary

All KeyLoad-owned backend, clients, contracts, frontend, tests, infrastructure and
docs live here and are versioned together. Independently owned ManagedCode
dependencies remain in their owning repositories; repair, release and verified
NuGet publication follow root policy.

| Project/module | Purpose and entry points | Canonical slices / protected boundary |
|---|---|---|
| src/KeyLoad.Abstractions | CommandRequest.cs, PartitionRef.cs, Mutation.cs and `<SliceName>/` contract folders | Shared public and storage contracts for every canonical slice. |
| src/KeyLoad.Core | DatabaseEngine.cs, AtomicCommandCommit.cs, CommandOutcomeApplication.cs, DocumentStorage/Execution/DocumentStorageExecutionDocuments.cs, Messaging/Execution/MessagingExecutionMessaging.cs, GraphTraversal/, TimeSeries/, Search/ | Shared node-local execution context and feature-owned operation components; authorization and model effects share the ordered atomic commit. |
| src/KeyLoad.Diagnostics | Models/DatabasePhaseKind.cs, Models/DatabasePhaseSnapshot.cs | BCL-only scalar phase observations, with no database state, public control endpoint or framework dependency. |
| src/KeyLoad.Storage.ZoneTree | ZoneTreeStore.cs, StorageEngine/Execution/ZoneTreeStoreRuntime.cs, StorageEngine/Recovery/ZoneTreeCheckpointManager.cs | StorageEngine, PointCache and Restore; files, WAL, checkpoints and local archives stay with the silo-local ZoneTree owner. |
| src/KeyLoad.Query | QueryExecution/Queries/QueryEngine.cs, QueryExecution/Execution/SqlParser.cs, Search/Queries/QueryExecution/SearchEngine.cs, ChangeFeeds/Queries/LiveQueryExecutor.cs | QueryExecution, Search, ChangeFeeds; one authorized typed AST and bounded read cut. |
| src/KeyLoad.Clustering | RequestExecution/Grains/ConnectionGrain.cs, RequestExecution/Grains/RequestExecutionGrain.cs, RequestExecution/Queries/Connections/ConnectionReadExecution.cs, `<SliceName>/GrainServices/` | OrleansExecution; connection and independent request grains route through Orleans to the silo-local ZoneTree owners. |
| src/KeyLoad | Program.cs, NodeOptions.cs, ClientApi/, OrleansHosting/Hosting/OrleansNode.Composition.cs | Composition root, Orleans host and public API; caller identity never supplies trusted roles. |
| src/KeyLoad.Client | ClientApi/Transport/KeyLoadClient.cs, QueryExecution/Queries/KeyLoadQuery.cs | ClientApi transport; business operations mirror the same canonical slices as core, contracts, API and tests. |
| src/KeyLoad.Cli | Program.cs, ClientApi/Transport/CliClientApi.cs, Restore/Commands/CliBackupRestore.cs | Administrative entry point with typed ClientApi and backup/restore owners. |
| build/KeyLoad.AppHost | Program.cs, Hosting/, OrleansHosting/Resources/ClusterTelemetryResources.cs, TestInfrastructure/ | Aspire owns cluster resources, readiness, test dependencies, scoped process faults and joined shutdown. |
| tests/KeyLoad.FunctionalTests | TestDatabase.cs and `<SliceName>/` folders | Matching product slices; contract and engine regressions. |
| tests/KeyLoad.ProcessRecoveryTests | Program.cs, StorageRecovery/Cases/, feature-owned Processes/ and Execution/ | TUnit recovery tests and their real child-process scenarios in the same test assembly. |
| tests/KeyLoad.ClusterTests | ClusterFixture.cs, StorageReplication/Cases/Cluster/ClusterTests.cs, ResourceExecution/Cases/RequestBodyLimitClusterTests.cs | Aspire-owned cluster fixtures; business cases mirror their owning slices through the real .NET SDK and official MCP client. |
| benchmarks/KeyLoad.ComparisonHost | Deployment/Hosting/BenchmarkDeployment.cs, Measurement/, `<Type>/{Dataset,Scenario,Cases,Drivers}` | BenchmarkComparisons; TUnit benchmark client: one shared dataset, scenario and driver interface per data type, and one result file per job. |
| benchmarks/KeyLoad.Benchmarks | Program.cs | BenchmarkComparisons; local BenchmarkDotNet executable for development measurements, never published. |
| benchmarks/KeyLoad.BenchmarkScenarios | Benchmarks/EmbeddedBenchmarks.cs | BenchmarkComparisons; [ADR-047](ADR/ADR-047-embedded-benchmark-host.md) public fixture library for the external generated consumer. |
| build/KeyLoad.LoadTests.AppHost | Program.cs, Resources/BenchmarkTopology.cs, Configuration/BenchmarkDatabases.cs | BenchmarkComparisons; Aspire deployment of the one database and node count a benchmark job selects. |
| site | Features/BenchmarkComparisons/index.html, results-merge.mjs, results-view.mjs, results-controls.mjs; scripts/results.mjs, scripts/build.mjs | BenchmarkComparisons; static product page with one comparison table per data type, rendered at build time from one merged results file. |
| tests/KeyLoad.WebsiteTests | KeyLoad.WebsiteTests.csproj, Cases/, Fixtures/Results/ | BenchmarkComparisons; TUnit suite that runs the real merge and build scripts through Node on test result files. |
| .github/workflows | build-and-tests.yml, benchmarks.yml, website.yml, release.yml | RepositoryGovernance, BenchmarkComparisons, ReleaseDelivery; separate build/test, benchmark, website and release workflows. |
| scripts | Features/RepositoryGovernance/verify.mjs, Features/CodeQuality/, Features/TestInfrastructure/, Features/ReleaseDelivery/ | RepositoryGovernance, CodeQuality, TestInfrastructure and ReleaseDelivery; static repository verification, coverage, test-entry and release tooling. |
| analyzers/KeyLoad.Analyzers | KeyLoad.Analyzers.csproj, Analyzers/, Analysis/ | CodeQuality; source-owned Roslyn diagnostics attached to compilation, never runtime storage or routing. |
| tests/KeyLoad.AnalyzerTests | KeyLoad.AnalyzerTests.csproj, Cases/, Fixtures/ | CodeQuality; real Roslyn/Orleans metadata checks. |
| docs | design/, Features/, ADR/, implementation/ | Product specification and canonical slice/decision records. |

```mermaid
flowchart TB
    SDK[Public .NET SDK and CLI] --> Server[Server gateway and identity]
    MCP[Official MCP SDK surface] --> Server
    Server --> Orleans[Connection and partition grains]
    Orleans --> Owners[Silo-local ZoneTree owner on every silo]
    Owners --> Core[Atomic engine and ordered apply]
    Core --> Storage[ZoneTree journals and files]
    Core --> Models[Documents events queues graph search series blobs]
    Aspire[Aspire Docker lifecycle] --> Owners
    CI[GitHub Actions qualification] --> Aspire
    CI --> JSON[Benchmark result file per job]
    JSON --> Site[Per-type tables on the Pages site]
```

## Feature ownership convention

Every C# project under `src/`, `tests/` and `benchmarks/` places named slices
directly under its project directory as `<project>/<SliceName>/<Role>/`. Each
slice groups actual responsibilities in populated role folders such as
`Grains/`, `Commands/`, `Queries/`, `Models/`, `Contracts/`, `Streaming/`,
`Identity/`, `Serialization/` and `Topology/`; a slice is an ownership boundary,
not a flat file collection. Test support uses explicit `Scenarios/`, `Fixtures/`,
`Assertions/`, `Contracts/`, `Lifecycle/`, `Observations/` and `Serialization/`
roles, with related subjects grouped beneath the role (for example
`Search/Scenarios/FullText/`); thematic folders stay inside `Cases/`. Scripts,
site and documentation keep their module paths. Namespaces, Orleans aliases and
field IDs stay stable across structural moves. The owner contract and checks are
in [RepositoryGovernance](Features/RepositoryGovernance.md) and
[ADR-032](ADR/ADR-032-mcaf-governance.md).

Canonical slice names use PascalCase consistently: RepositoryGovernance,
BenchmarkComparisons, DatabaseComposition, DocumentStorage, RelationalStorage,
EventStreams, Messaging, GraphTraversal, TimeSeries, Search, QueryExecution,
Authorization, ChangeFeeds, StorageRecovery, StorageReplication, OrleansExecution,
ClientApi, BackupRestore (project folder `Restore/`), BlobStorage,
ResourceExecution, InternalSerialization, AdminDashboard, AnalyticsStore,
CodeQuality, ReleaseDelivery and TestInfrastructure. Their owning contracts and
navigation are in the [Feature index](README.md). Shared entry points,
composition roots, truly shared contracts and global infrastructure may remain
outside slice folders. A non-applicable surface is recorded as N/A with its
reason in the feature spec. Shared atomic commit and outcome dispatch stay
outside slice folders because they join multiple mutation families.

## Shared request contracts

```mermaid
sequenceDiagram
    participant Caller as SDK or MCP caller
    participant API as Public API
    participant Conn as Connection grain
    participant Part as Request grain
    participant Owners as ZoneTree owners on ReplicaCount silos
    Caller->>API: Typed operation and credentials
    API->>Conn: Verified subject and bounded request
    Conn->>Part: Admitted operation
    Part->>Part: Fresh persisted authorization
    Part->>Owners: Orleans calls with the command
    Owners-->>Part: Durable local apply confirmations
    Part-->>Conn: Outcome after every configured copy confirms
    Conn-->>API: Public result
    API-->>Caller: Contract result
```

```mermaid
classDiagram
    class KeyLoadClient
    class CommandRequest
    class PartitionRef
    class ConnectionGrain
    class RequestGrain
    class RequestExecutionGrain
    class DatabaseEngine
    class IKeyValueView
    class IAtomicTransaction
    KeyLoadClient --> CommandRequest
    CommandRequest --> PartitionRef
    ConnectionGrain --> RequestGrain : one per operation
    RequestGrain --> DatabaseEngine : borrows the local owner through DI
    RequestGrain --> RequestExecutionGrain : leaf on a remote owner silo
    RequestExecutionGrain --> DatabaseEngine : borrows that silo's owner through DI
    DatabaseEngine --> IKeyValueView
    DatabaseEngine --> IAtomicTransaction
```

Official MCP and simple agent adapters belong to [ClientApi](Features/ClientApi.md)
and [ADR-039](ADR/ADR-039-official-mcp-agent-api.md); business operations keep
their own Feature owners. The MCP host exposes bounded search/route/invoke tools
through ManagedCode.MCPGateway and ManagedCode.MarkdownLd.Kb tool-metadata graphs
([ToolDiscovery](Features/ClientApi/ToolDiscovery.md),
[ADR-104](ADR/ADR-104-mcp-gateway-tool-discovery.md)); the graph holds public
operation documentation only. The connection grain consumes the bounded
ManagedCode.Communication CQRS producer locally for ordinary operations and
returns its settled terminal reply through one Orleans Task call. Progress
consumers and composed operations retain Orleans `IAsyncEnumerable` responses
from the same producer ([RuntimeCqrs](Features/OrleansExecution/RuntimeCqrs.md),
[ADR-082](ADR/ADR-082-cqrs-streams.md)). Response transport does not move
node-owned storage or replace persisted authority.

## SQL and relational model

[ADR-132](ADR/ADR-132-create-table-command-identity.md) defines the
[bounded CREATE TABLE operation](Features/RelationalStorage/CreateTable.md) with a
caller-stable SQL command identity on the connection-owned execution path.
Canonical typed resources stay in ZoneTree behind the ordered apply gate. The
PostgreSQL wire/Npgsql session is a separately qualified transport, not a
PostgreSQL grammar or SQL Server TDS promise.

```mermaid
classDiagram
    ScriptDomSyntax --> TSqlParser : bounded grammar
    SqlOperationCompiler --> ScriptDomSyntax : selected syntax
    SqlOperationCompiler --> SqlOriginalMutationIdentity : original command identity
    SqlOperationCompiler --> McpDecodedOperation : canonical typed plan
    ScriptDomCreateTableOperation --> ScriptDomCreateTableSyntax : closed schema
    CanonicalConnectionDispatch --> McpDecodedOperation : authorized execution
```

```mermaid
flowchart LR
    Agent[AI agent SQL SDK MCP] --> Server[One server API]
    Server --> SQL[Versioned TSQL SELECT and EXEC compiler]
    SQL --> Gateway[Connection grain]
    Gateway --> Owners[Partition grains and silo-local ZoneTree owners]
    Owners --> Entity[Canonical documents and typed rows]
    Owners --> Models[Graph search events queues series blobs]
    Entity --> Models
```

```mermaid
classDiagram
    ResourceDefinition --> RelationalSchema
    RelationalSchema --> RelationalColumn
    SqlOperationRequest --> SqlOperationCompiler
    SqlOperationCompiler --> McpOperationDescriptor : canonical typed decoder
    McpOperationDescriptor --> CanonicalOperationGateway
    RelationalRowValidation --> DocumentRecord : validates final row image
```

[ResourcePolicyUpdates](Features/Authorization/ResourcePolicyUpdates.md) and
[ADR-093](ADR/ADR-093-resource-policy-updates.md) define policy-only CAS through
the replicated ConfigureResource path; advancing a resource version invalidates
existing cursors and index fences, and physical schema changes stay separate.

## Storage, atomic commit and recovery

File API contracts and server transfers use ManagedCode.Storage. ZoneTree stores
file metadata, access policy, publication state and physical-storage references;
provider-owned physical files contain the payload bytes. Database command and
replica journals carry metadata, never file content. See
[ADR-038](ADR/ADR-038-chunked-blob-storage.md) and
[BlobStorage](Features/BlobStorage.md) for the publication boundary and open
physical-provider qualification gates.


ZoneTree is the canonical storage for every KeyLoad-owned model and
ZoneTree.FullTextSearch is the selected derived text index
([ADR-071](ADR/ADR-071-canonical-zonetree-providers.md),
[FullTextProjection](Features/Search/FullTextProjection.md)). The
silo-local ZoneTree owner keeps WAL, ordered apply and file ownership on
its node. Current storage accepts only the formats in
[CurrentFormat](Features/StorageRecovery/CurrentFormat.md) and rejects
unsupported or corrupt versions before effects
([ADR-011](ADR/ADR-011-current-format.md),
[ADR-116](ADR/ADR-116-first-release-current-format.md)). Process-kill recovery
tests do not prove power-loss durability.

Document CRUD handlers and transaction-scoped image carriers belong to
`Core/DocumentStorage/`; read budgets, the analytical-read gate and cache
reservations belong to `Core/ResourceExecution/`. `DatabaseEngine` holds the
shared storage context, validated policy snapshots and its analytical-read gate. Feature-owned static
operation components contain the command, query and validation implementations;
extension methods pass that same context explicitly. They preserve the existing
atomic transaction and persisted authorization boundary. Local numeric checks
and complete runtime qualification are separate gates
([ADR-041](ADR/ADR-041-read-only-public-collections.md)).

```mermaid
classDiagram
    class DatabaseEngine
    class DocumentMutationContext
    class DocumentMutationResult
    class DocumentRecord
    class MutationReceipt
    class IAtomicTransaction
    DatabaseEngine --> IAtomicTransaction : atomic command gate
    DatabaseEngine --> DocumentMutationContext : one prior lookup
    DocumentMutationContext --> DocumentRecord : Before
    DatabaseEngine --> DocumentMutationResult : CRUD return
    DocumentMutationResult --> DocumentRecord : staged After
    DocumentMutationResult --> MutationReceipt : exact receipt
```

```mermaid
classDiagram
    ZoneTreeTransaction --> ZoneTreeJournalCodec : prepares binary payload
    ZoneTreeJournalPublication --> ZoneTreeJournalCodec : validated payload
    ZoneTreeJournalRecovery --> ZoneTreeJournalCodec : complete checked decode
    ZoneTreeJournalCodec --> ZoneTreeJournalMutation : stable generated fields
    ZoneTreeStoreInitializer --> ZoneTreeIdentityFile : current format writer fence
```

[RuntimeReadCuts](Features/StorageRecovery/RuntimeReadCuts.md) under
[ADR-097](ADR/ADR-097-read-cut-leases.md) supplies provider-owned snapshot
lifetimes for bounded reads. A real-process inspector checks an original store
without the normal runtime, then releases it for a normal reopen:

```mermaid
classDiagram
    class ExistingStoreInspectorProcess
    class ExistingStoreInspector
    class ExistingStoreInspectorProtocol
    class ZoneTreeExistingStore
    ExistingStoreInspectorProcess --> ExistingStoreInspectorProtocol : bounded stdin
    ExistingStoreInspector --> ExistingStoreInspectorProtocol : safe receipt
    ExistingStoreInspector --> ZoneTreeExistingStore : owns guarded runtime
    ExistingStoreInspectorProcess --> ExistingStoreInspector : original child lifetime
```

Public chunked blobs and partial reads belong to
[BlobStorage](Features/BlobStorage.md) and
[ADR-038](ADR/ADR-038-chunked-blob-storage.md); backup archive pieces are a
separate storage protocol.

## Internal serialization

[ADR-060](ADR/ADR-060-internal-serialization.md) and
[InternalSerialization](Features/InternalSerialization.md) require execution
generated Orleans payloads with stable aliases and field IDs across owned
persistence, grain requests/replies, copies sent to ZoneTree owners and signed
claims. Public HTTP/MCP JSON, exact user content, frozen canonical fingerprint
identities, sortable keys, raw ZoneTree values, fixed checksum framing and blob
bytes keep their concrete protocols. Grain requests and replies carry generated
typed values directly. Requests retain their HTTP/MCP frame limits and Orleans
keeps its default message size limits; replies are not measured
([ADR-149](ADR/ADR-149-no-operation-limits.md)). They contain no private encoded operation/result buffer. Generated bytes are not canonical
hashes for unordered values. Readers reject unsupported formats before effects.

```mermaid
flowchart LR
    Public[Public JSON protocol] --> Typed[Attributed owned DTOs]
    Typed --> Execution[Cached Orleans codecs and pooled sessions]
    Execution --> Records[Typed records and versioned metadata]
    Execution --> Calls[Grain call payloads and copies]
    Execution --> Claims[Signed claims]
    Records --> Files[Node local ZoneTree and atomic WAL]
    Typed --> Identity[Frozen canonical fingerprint material]
```

```mermaid
classDiagram
    OrleansSerialization --> RuntimePayload : versioned generated root
    OrleansSerialization --> OrleansSerializer : official generated codecs
    OrleansSerializer --> OfficialJsonCodec : JsonElement only
    ZoneTreeIdentityFile --> ZoneTreeMetadataBinary : explicit format fence
    GrainRequestEnvelope --> GrainValue : attributed boundary
```

## Query, search and read budgets

[ResourceExecution](Features/ResourceExecution.md) and
[ADR-035](ADR/ADR-035-memory-performance.md) own the common operation context
(cancellation, explicit expiry, resource lifetimes) in shared node-local
building blocks; there are no read-byte or result-byte budgets; operation behavior stays in its canonical slice.
[ClientApi](Features/ClientApi.md) owns HTTP response streaming. Scoped document
and vector visitors feed QueryExecution and Search without raw pages;
GraphTraversal keeps cut-scoped visibility decisions. The shared canonical JSON
writer streams fingerprints to a hash and preserves exact signatures.

```mermaid
classDiagram
    class KeyLoadClient
    class QueryEngine
    class LiveQueryExecutor
    class DatabaseEngine
    class ReadExecutionBudget
    class VisibleDocumentReads
    class VisibleVectorReads
    class GraphTraversalReader
    class JsonData
    class CanonicalJsonWriter
    class CanonicalHashStream
    class IKeyValueView
    class ZoneTreeStore
    class ZoneTreeStoreRuntime
    class ZoneTreeTransaction
    class ZoneTreeCheckpointManager
    class ZoneTreeCheckpointReader
    class ZoneTreeBackupRestore
    KeyLoadClient --> QueryEngine : public HTTP operation
    QueryEngine --> ReadExecutionBudget : scoped budget
    QueryEngine --> LiveQueryExecutor : live public facade
    LiveQueryExecutor --> DatabaseEngine : same authorized read cut
    LiveQueryExecutor --> QueryEngine : borrowed execution and projection
    QueryEngine --> VisibleDocumentReads : visible candidates
    VisibleDocumentReads --> ReadExecutionBudget : borrowed scan
    VisibleVectorReads --> ReadExecutionBudget : vector and document work
    GraphTraversalReader --> ReadExecutionBudget : visibility reads
    JsonData --> CanonicalJsonWriter : exact canonical bytes
    CanonicalJsonWriter --> CanonicalHashStream : incremental fingerprint
    ReadExecutionBudget --> IKeyValueView : cancellation-checked reads
    ZoneTreeStore ..|> IKeyValueView : gate scoped implementation
    ZoneTreeStore --> ZoneTreeStoreRuntime : sole node local owner
    ZoneTreeStoreRuntime --> ZoneTreeTransaction : staged atomic work
    ZoneTreeStoreRuntime --> ZoneTreeCheckpointManager : generation replacement
    ZoneTreeCheckpointManager --> ZoneTreeCheckpointReader : strict format
    ZoneTreeStoreRuntime --> ZoneTreeBackupRestore : verified local backup
```

search scheduling follows
[ADR-081](ADR/ADR-081-awaited-search-execution.md): one analytical
reservation spans the complete awaited read and settlement while the
connection grain yields. Index generations have bounded leases and
retirement ([OnlineGenerationLifetime](Features/Search/OnlineGenerationLifetime.md));
global retrieval merges bounded per-modality windows before RRF
([GlobalBranchWindows](Features/Search/GlobalBranchWindows.md)). SIMD execution
uses execution .NET vector intrinsics with scalar code only for bounded tails and
independent correctness oracles ([Search](Features/Search.md)); acceleration
claims need comparable GitHub measurements.

[ADR-019](ADR/ADR-019-managed-ann.md), [ManagedAnn](Features/Search/ManagedAnn.md)
and [ManagedAnnSeed](Features/Search/ManagedAnnSeed.md) define the managed ANN
candidate: a private builder copies bounded canonical vector input captured in
one node-local read cut into owned packed arrays, and each search uses its own
admitted scratch with the exact metric implementation. ANN projection updates
are background work under the StatelessWorker pattern above.

```mermaid
classDiagram
    AnnSeedCollector --> ReadExecutionBudget : one canonical cut
    AnnSeedCollector --> AnnSeed : owned snapshot input
    AnnSeed --> PackedAnnBuilder : later explicit integration
    AnnWorkBudget --> ReadExecutionBudget : work and elapsed checks
    PackedAnnBuilder --> PackedAnnGraph : actual level offsets
    PackedAnnBuilder --> PackedAnnVectors : owned bounded blocks
    PackedAnnBuilder --> PackedAnnIndex : complete immutable state
    PackedAnnIndex --> AnnWorkBudget : per call admission
    PackedAnnIndex --> PreparedSimilarity : unchanged scores
    PackedAnnIndex --> AnnSearchResult : candidates mode and counters
```

## Time series

[ADR-052](ADR/ADR-052-timeseries-bounded-aggregates.md) defines the
[TimeSeries](Features/TimeSeries.md) latest/aggregate/window slice. Typed
Abstractions DTOs mirror Client, Server and Core owners. Core uses the published
ManagedCode.TimeSeries library inside one budgeted storage cut, and shared
StorageRecovery reverse visitors support bounded latest reads. Chunk work runs
under the StatelessWorker background pattern.

```mermaid
classDiagram
    class TimeSeriesReadOperations
    class SampleLatestReader
    class SampleAggregateReader
    class SampleAggregateWindowReader
    class SampleAggregateAccumulator
    class BudgetedReadView
    class ZoneTreeRangeReader
    TimeSeriesReadOperations --> BudgetedReadView : one gated operation budget
    TimeSeriesReadOperations --> SampleLatestReader
    TimeSeriesReadOperations --> SampleAggregateReader
    TimeSeriesReadOperations --> SampleAggregateWindowReader
    SampleLatestReader --> ZoneTreeRangeReader : descending first live key
    SampleAggregateReader --> SampleAggregateAccumulator : published sums
    SampleAggregateWindowReader --> SampleAggregateAccumulator : bounded windows
```

## Memory and caches

[ADR-058](ADR/ADR-058-orleans-coordinated-cache-memory.md) keeps RAM acceleration
under the ResourceExecution slice. The common retained pool and the embedded
positive point-value cache are disposable node-local state over committed
ZoneTree data. Cached state never replaces persisted authorization, committed
ZoneTree state or the acknowledgement from every configured copy of copies, and it never moves
files or handles.

```mermaid
classDiagram
    class ICacheMemoryBudget {
        TryReserve(bytes, entries)
        GetSnapshot()
    }
    class ICacheMemoryReservation {
        Bytes
        Entries
        Dispose()
    }
    class CacheMemoryBudget
    class CacheMemoryLimits
    CacheMemoryBudget ..|> ICacheMemoryBudget
    ICacheMemoryBudget --> ICacheMemoryReservation
    CacheMemoryBudget --> CacheMemoryLimits
    class ICacheReadPermit {
        IsCurrentAcceptance(receipt)
    }
    ZoneTreeStore --> ZoneTreeStoreRuntime
    ZoneTreeStoreRuntime --> ZoneTreePointCacheLifecycle
    ZoneTreePointCacheLifecycle --> ZoneTreePointCacheControl
    ZoneTreePointCacheControl --> ZoneTreePointCacheControlState
    ZoneTreePointCacheControlState --> ICacheReadPermit
    ZoneTreePointCacheControlState --> ZoneTreePointCacheBinding
    ZoneTreePointCacheBinding --> ZoneTreePointCache
    ZoneTreePointCache --> ICacheMemoryBudget
```

## Diagnostics

[ADR-063](ADR/ADR-063-bounded-database-phase-profiling.md) defines the optional
bounded database phase bank. Process mode is disabled by default; private
capture runs outside database gates.

```mermaid
classDiagram
    class DatabasePhaseKind
    class DatabasePhaseOutcome
    class DatabasePhaseBank
    class DatabasePhaseSnapshot
    DatabasePhaseKind --> DatabasePhaseBank : fixed source boundary
    DatabasePhaseOutcome --> DatabasePhaseBank : actual owned result
    DatabasePhaseBank --> DatabasePhaseSnapshot : detached cumulative capture
```

## Admin console and website identity

The [AdminDashboard](Features/AdminDashboard.md) slice under
[ADR-051](ADR/ADR-051-admin-dashboard.md) hosts the read-only console in
`src/KeyLoad/AdminDashboard/Assets/`, with shared Abstractions DTOs, Core
catalog/queue readers and a matching Client SDK. It uses the connection grain
with call-local authorized reads; physical observations are reported per node.
The public `site/` is its own surface. Both share one visual identity under
[ADR-053](ADR/ADR-053-unified-visual-identity.md): the console owns the canonical
`brand.css`/`logo.svg` and the site keeps byte-identical mirrors, including the
PNG/ICO icon exports. The landing page carries static SEO/OG/JSON-LD metadata and
a share card.

```mermaid
flowchart LR
    Admin[Same origin admin console] --> API[AdminDashboard API and MCP]
    API --> Request[Connection grain]
    Request --> Read[Call-local authorized read execution]
    Read --> Core[Bounded catalog and queue readers]
    Read --> Node[Node local file and process observer]
```

## Code quality

[CodeQuality](Features/CodeQuality.md) is a compile-time slice defined by
[ADR-033](ADR/ADR-033-code-quality.md). Central SDK/style analysis and
source-owned Roslyn rules produce compiler/IDE findings and per-project SARIF
reports; builds always run with warnings as errors. This build infrastructure has
no database runtime or persistence surface.

```mermaid
flowchart LR
    Rules[CodeQuality rule sources] --> Analyzer[KeyLoad.Analyzers]
    Analyzer --> Compiler[SDK Roslyn compiler and IDE]
    Policy[EditorConfig and central MSBuild] --> Compiler
    Projects[Solution projects] --> Compiler
    Compiler --> SARIF[Compiler diagnostic reports]
    Tests[Real compilation TUnit regressions] --> Analyzer
```

```mermaid
classDiagram
    class DiagnosticAnalyzer
    class LiteralMachineKeyAnalyzer
    class OrleansGenerateSerializerAnalyzer
    class CSharpCompilation
    class Diagnostic
    DiagnosticAnalyzer <|-- LiteralMachineKeyAnalyzer
    DiagnosticAnalyzer <|-- OrleansGenerateSerializerAnalyzer
    CSharpCompilation --> DiagnosticAnalyzer
    DiagnosticAnalyzer --> Diagnostic
```

## Benchmark comparisons

[BenchmarkComparisons](Features/BenchmarkComparisons.md) under
[ADR-150](ADR/ADR-150-per-type-benchmark-jobs.md) runs one GitHub job per
database, data type and node count. A job builds
`benchmarks/KeyLoad.ComparisonHost`; its shared TUnit fixture starts the one
selected database through `build/KeyLoad.LoadTests.AppHost`, runs the cases of its
type in order on that live deployment and rewrites one result file after every
case. Each data type has one shared dataset, scenario and driver interface; every
database, KeyLoad included, implements only the driver. KeyLoad runs the released
server image pinned to the digest the run resolved.

```mermaid
flowchart LR
    Image[Released KeyLoad image digest] --> Job[One job per database, type and nodes]
    Job --> AppHost[LoadTests AppHost deploys the database]
    AppHost --> Cases[Type scenario cases on one live deployment]
    Cases --> File[Result file after every case]
    File --> Website[Website merges the run and renders per-type tables]
```

The graph type shows the shape every type shares:

```mermaid
classDiagram
    class GraphBenchmarks
    class BenchmarkDeployment
    class BenchmarkApplication
    class GraphScenario
    class IGraphDriver
    class OperationLanes
    class BenchmarkResult
    GraphBenchmarks --> BenchmarkDeployment : shared live deployment
    BenchmarkDeployment --> BenchmarkApplication : starts and connects
    GraphBenchmarks --> GraphScenario : runs each case
    GraphScenario --> IGraphDriver : database operations only
    GraphScenario --> OperationLanes : 16 concurrent clients
    BenchmarkDeployment --> BenchmarkResult : rewritten after every case
```

Embedded ZoneTree microbenchmarks
([ADR-047](ADR/ADR-047-embedded-benchmark-host.md)) and the raw read lane
([ADR-069](ADR/ADR-069-zonetree-raw-read-lane.md)) use the actual BenchmarkDotNet
generated consumer for local development and are never published.

```mermaid
classDiagram
    class BenchmarkProgram
    class EmbeddedBenchmarks
    class GeneratedBenchmarkRunner
    class ZoneTreeStore
    BenchmarkProgram --> EmbeddedBenchmarks : BenchmarkSwitcher selects assembly
    GeneratedBenchmarkRunner --|> EmbeddedBenchmarks : upstream generated inheritance
    EmbeddedBenchmarks --> ZoneTreeStore : owns actual embedded fixture storage
```

## Delivery workflows

[ADR-064](ADR/ADR-064-workflow-release-delivery.md) and
[ADR-062](ADR/ADR-062-workflow-separation.md) define exactly four workflows.
`build-and-tests.yml` (`Build and Tests`) owns PR/main/manual build, format,
rules, analyzers, unit, process recovery and Docker/Aspire cluster SDK/MCP tests;
benchmark jobs run only in Benchmarks
([ADR-117](ADR/ADR-117-tunit-ci-entry.md)). `benchmarks.yml`
(`Benchmarks`) resolves the released KeyLoad image to one digest, runs one job per
database, data type and node count, uploads each job's result file whether it
passed or failed and then dispatches `website.yml` (`Website`) with its run id
([ADR-150](ADR/ADR-150-per-type-benchmark-jobs.md)). Website downloads the result
files of that run, or of the latest completed run on a site push or manual
dispatch, merges them, builds the static site and deploys Pages; without result
files it publishes nothing. `release.yml` (`Release`) stays manual
([ReleaseDelivery](Features/ReleaseDelivery.md)): self-contained server/CLI
archives and the server image published by the .NET SDK `PublishContainer` target
to `ghcr.io/managedcode/keyload` under the commit SHA and `latest` tags. Packaging
does not establish runtime or endurance qualification.

```mermaid
flowchart LR
    Source[PR or main source] --> CI[Build and Tests]
    Main[Main source] --> Benchmarks[One job per database, type and nodes]
    Benchmarks --> Files[Result file artifacts]
    Files --> Dispatch[Website dispatch with the run id]
    Dispatch --> Site[Merge, static build and Pages]
    SiteSource[Site source push] --> Site
    Manual[Manual main release] --> Build[Self-contained archives and server image]
    Build --> Registry[GHCR server image SHA and latest]
    Registry --> Benchmarks
```

Delivered-source qualification comes only from exact-source Linux GitHub runs
with their original artifacts; local runs are development evidence.
