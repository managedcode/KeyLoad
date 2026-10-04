# KeyLoad architecture and ownership map

Owner clarification 2026-10-03 makes composition the core product:
**one database for AI agents**, where documents, typed tables, graphs, blobs,
queues, events, vectors/search and time series coexist and reference one another.
SQL is the familiar shared language for combining those models in one request.
The motivating flows are queue messages -> referenced entities -> knowledge graph,
and graph relationships -> queued actions. Logical resources organize one database.
[DatabaseComposition](Features/DatabaseComposition.md) and
[ADR-067](ADR/ADR-067-composable-agent-database.md) own the first bounded server
derivation stage through the existing atomic command and SQL CALL. Full
declarative SQL and client protocol remain mandatory under ADR-065.

```mermaid
flowchart LR
    Caller[Agent SQL SDK MCP] --> Request[One authorized Orleans request]
    Request --> Tx[Same partition ordered RF3 ZoneTree transaction]
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
    DatabaseEngine --> ReadExecutionBudget : cumulative deterministic bounds
```

Owner clarification2026-10-02: KeyLoad is one server/database for AI agents with
linked documents, relational rows, graph, vectors/text search, events, queues,
time series and files/blobs. SQL is the central versioned language. The first
unified invocation/typed-row stage is specified by [ADR-054](ADR/ADR-054-central-sql.md)
and [ADR-055](ADR/ADR-055-typed-relational-rows.md); qualification remains pending.
Typed rows reuse Collection entity storage so graph/vector references retain one
identity. [RelationalStorage](Features/RelationalStorage.md) defines schemas and
native constraints; QueryExecution owns the SQL adapter over canonical operations.

Owner clarification2026-10-03 requires full SQL and a client connection protocol.
[ADR-065](ADR/ADR-065-full-sql-client-compatibility.md) extends the existing stages
with explicit typed execution/native-client gates and
[versioned conformance inventory](implementation/sql-client-conformance.json).
The first lexical repair shares an internal bounded trivia reader between Query,
Server and SDK; quoted content, authority, envelope1 and storage remain exact.
Full PostgreSQL-compatible execution and native transport are pending. The
[ZoneTree.FullTextSearch candidate review](implementation/zonetree-fulltextsearch-review.md)
adds search integration/correctness/recovery/resource/performance gates without
selecting a provider or promising acceleration.

[ADR-068](ADR/ADR-068-native-benchmark-gate-repair.md) preserves isolated benchmark
startup authority: original bounded job discovery joins authenticated metadata for
the same job ID before native allocation. Still-queued metadata has three bounded
captures; identity/state failures remain failures. Kurrent metadata/leader gates
and complete native cohort/site qualification remain open.

```mermaid
classDiagram
    SqlParser --> SqlTriviaReader : bounded token trivia
    SqlOperationSyntaxReader --> SqlTriviaReader : canonical CALL trivia
    SqlWriteClassifier --> SqlTriviaReader : conservative SDK outcomes
    SqlOperationCompiler --> CanonicalOperationGateway : unchanged authority
```

```mermaid
flowchart LR
    Agent[AI agent SQL SDK MCP] --> Server[One server API]
    Server --> SQL[Versioned SELECT and CALL compiler]
    SQL --> Gateway[Existing signed Orleans gateway]
    Gateway --> Host[Node local PartitionHost RF3]
    Host --> Entity[Canonical documents and typed rows]
    Host --> Models[Graph search events queues series blobs]
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

The additive [AdminDashboard](Features/AdminDashboard.md) slice under [ADR-051](ADR/ADR-051-admin-dashboard.md) hosts the runtime read-only console in `src/KeyLoad.Server/Features/AdminDashboard/Assets/`, with shared Abstractions DTOs, Core catalog/queue readers, matching Client SDK and UnitTests/IntegrationTests slices. It borrows the existing unique Orleans request/read actors and node-local administration; physical observations are explicitly per node. The public benchmark `site/` remains its own surface. Both surfaces share one light visual identity under [ADR-053](ADR/ADR-053-unified-visual-identity.md): the console owns the canonical `brand.css`/`logo.svg`, and the site keeps byte-identical mirrors checked by SiteTests. Exact-SHA dashboard unit/RF3/native browser cases pass and retained desktop/mobile images are visually reviewed; complete recovery/comparison and numeric coverage qualification remains open.

[SiteMetadata](Features/BenchmarkComparisons/SiteMetadata.md) extends that identity
with mirrored PNG/ICO exports, static landing SEO/OG/Twitter/JSON-LD and an authored
share card. The existing static builder owns root delivery and crawl files; the
console's finite embedded shell owns only its exact icon routes and noindex.
Source assets and local rendering do not establish indexing or publication.

```mermaid
flowchart LR
    Admin[Same origin admin console] --> API[AdminDashboard API and MCP]
    API --> Request[Unique request grain]
    Request --> Read[Admin authorized read grain]
    Read --> Core[Bounded catalog and queue readers]
    Read --> Node[Node local file and process observer]
```

Read the root and nearest project-local AGENTS.md before changing this solution. The product specification is [architecture v0.3](design/architecture-v0.3.uk.md). This document is a navigation map, not a replacement specification or a readiness claim.

The [documentation index](README.md) is the complete entry point for 22 canonical Feature specifications. Each owning Feature defines stable REQ/AC, callers, boundaries, flows, existing or planned tests and a Mermaid diagram. The [ADR catalog](ADR/README.md) records decisions with status and implementation contracts. The [coverage catalog](implementation/documentation-coverage.json) maps all 104 KL tasks; [status.json](implementation/status.json) remains the single implementation-status authority.

Current mandatory policy requires an Orleans RF3 database, node-local PartitionHost storage ownership, separate request grains, distributed grain directory and activation migration, TUnit tests, Docker/Aspire RF3 execution and real .NET SDK plus official MCP SDK callers. Atomic partitions remain separate from physical replica placement. Credentials and trusted authorization are persisted server-side.

The owner also requires ManagedCode.Communication native CQRS IAsyncEnumerable
results and long operations through Orleans. [NativeCqrs](Features/ClusterRouting/NativeCqrs.md)
and [ADR-082](ADR/ADR-082-native-cqrs-streams.md) first qualify the actual Graph
filter/enumerator context and cleanup join. Current request RPCv1 returns bounded
Task replies; the stream RPC/transport and durable-operation contracts remain
pending. Native enumeration does not transfer node-owned storage or supply
persisted authority, retry receipts or durability.

[ResourcePolicyUpdates](Features/Authorization/ResourcePolicyUpdates.md) and
[ADR-093](ADR/ADR-093-resource-policy-updates.md) freeze policy-only CAS through
the existing RF3 metadata ConfigureResource path. Advancing its resource version
invalidates existing read/index fences; physical schema and placement remain
separate migrations. Implementation and public qualification are in progress.

[ADR-052](ADR/ADR-052-timeseries-bounded-aggregates.md) accepts the additive
[TimeSeries](Features/TimeSeries.md) latest/aggregate/window slice. New typed
Abstractions DTOs mirror Client/Server/Core TimeSeries owners and real matching
UnitTests/IntegrationTests. Core uses the published ManagedCode.TimeSeries library
inside one budgeted storage cut; shared StorageRecovery reverse visitors support
bounded latest. Orleans request routing and node-local storage ownership remain.
The additive source and real SDK/MCP regression paths are present; this contract
remains Accepted while exact-SHA full qualification and broader product gates are
retained separately in implementation status.

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
    SampleAggregateReader --> SampleAggregateAccumulator : published native sums
    SampleAggregateWindowReader --> SampleAggregateAccumulator : bounded windows
```

## Complete repository boundary

The optional bounded diagnostic workstream is accepted in
[ADR-063](ADR/ADR-063-bounded-database-phase-profiling.md). The fixed bank and
17 actual public/request/replication/provider phase joins are implemented in source. Process
mode remains disabled by default; the remaining15 joins, private capture and
native phase/resource measurements remain open.

```mermaid
classDiagram
    class DatabasePhaseKind
    class DatabasePhaseOutcome
    class DatabasePhaseBank
    class DatabasePhaseSnapshot
    class PrivateNodeExporter
    DatabasePhaseKind --> DatabasePhaseBank : fixed source boundary
    DatabasePhaseOutcome --> DatabasePhaseBank : actual owned result
    DatabasePhaseBank --> DatabasePhaseSnapshot : detached cumulative capture
    PrivateNodeExporter --> DatabasePhaseSnapshot : outside database gates
```

All KeyLoad-owned backend, clients, contracts, frontend, tests, infrastructure and docs live here and are versioned together. Independently owned ManagedCode dependencies remain in their owning repositories; repair/release/verified NuGet publication follows root policy.

| Project/module | Purpose and entry points | Canonical slices / protected boundary |
|---|---|---|
| src/KeyLoad.Abstractions | Contracts.cs, Features/<SliceName>/Contracts/, Storage/StorageContracts.cs | Shared public/storage contracts; DocumentStorage, EventStreams, Messaging, Authorization, Search, GraphTraversal. |
| src/KeyLoad.Core | DatabaseEngine.cs, Features/DocumentStorage/Execution/Documents.cs, Features/EventStreams/Execution/Events.cs, Features/Messaging/Execution/Messaging.cs, Features/GraphTraversal/, Features/TimeSeries/, Features/Search/ | Node-local engine and atomic transactions; document/event/queue/graph/time-series behavior. |
| src/KeyLoad.Diagnostics | Features/ResourceExecution/Models/DatabasePhaseKind.cs, Features/ResourceExecution/Models/DatabasePhaseSnapshot.cs and fixed bank | ResourceExecution; ADR-063 BCL-only callback-free scalar phase observations. No database state, public control endpoint or framework dependency; source integration and genuine native profiling pending. |
| src/KeyLoad.Storage.ZoneTree | ZoneTreeStore.cs; Features/StorageRecovery/Execution/ZoneTreeStoreRuntime.cs and Features/StorageRecovery/Recovery/ZoneTreeCheckpointManager.cs | StorageRecovery and BackupRestore; file/WAL/checkpoint ownership stays node-local. |
| src/KeyLoad.Storage.IO | Features/StorageRecovery/Storage/OfflineRegularFile.cs | StorageRecovery; shared internal public-OS-ABI regular-file opening for stopped migration. No engine, database state or replication dependency; ADR-077, AC-EPOCH-012. |
| src/KeyLoad.Security | Features/Authorization/Execution/AuthorizationPolicy.cs | Authorization; trusted principal and row/field policy enforcement. |
| src/KeyLoad.Query | Features/QueryExecution/Queries/QueryEngine.cs, Features/QueryExecution/Execution/SqlParser.cs, Features/Search/Queries/SearchEngine.cs; Features/ChangeFeeds/Queries/LiveQueryExecutor.cs behind Features/ChangeFeeds/Execution/LiveQueries.cs facade | QueryExecution, Search, ChangeFeeds; one authorized typed AST and bounded read cut. |
| src/KeyLoad.Artifacts | Features/BackupRestore/Execution/ArtifactTransfer.cs, Features/BackupRestore/Execution/BackupArtifact.cs | BackupRestore; verified artifact transport and format ownership. |
| src/KeyLoad.Replication | Features/ClusterReplication/Execution/ClusterCoordinator.cs, Features/ClusterReplication/Storage/DurableReplicaLog.cs, Features/ClusterReplication/Execution/ReplicaMaterializer.cs | ClusterReplication, StorageRecovery; ordered durable apply and quorum authority behind the active Orleans server composition; delivered-SHA RF3 qualification pending. |
| src/KeyLoad.Orleans | Features/ClusterRouting/Grains/{RequestGrain,DatabaseReadGrain,CommandPartitionGrain}.cs; Topology/ReplicaMembershipTable.cs | ClusterRouting; active server composition routes through grains while node-local hosts own storage; forced activation-migration qualification pending. |
| src/KeyLoad.Server | Program.cs, NodeOptions.cs, Features/ClientApi/, Features/ClusterRouting/Hosting/OrleansNode.cs and feature API files | Composition root and public API; caller identity never supplies trusted roles. |
| src/KeyLoad.Client | KeyLoadClient.cs, Features/QueryExecution/Queries/KeyLoadQuery.cs | ClientApi shared transport; business operations mirror the same canonical slices as core/contracts/API/tests. |
| src/KeyLoad.Cli | Program.cs, Features/ClientApi/Hosting/KeyLoadCliApplication.cs, Features/ClientApi/Transport/CliClientApi.cs and Features/BackupRestore/Commands/CliBackupRestore.cs | Composition-only administrative entry point and typed ClientApi/BackupRestore feature owners. |
| src/KeyLoad.ServiceDefaults | Extensions.cs | Shared telemetry, service discovery and resilience composition. |
| src/KeyLoad.AppHost | Program.cs, Features/BenchmarkComparisons/Resources/BenchmarkResources.cs | ClusterReplication, BenchmarkComparisons; Aspire owns resource lifecycle. |
| tests/KeyLoad.UnitTests | TestDatabase.cs and focused test files | Matching product slices; pure contract/engine regression evidence. |
| tests/KeyLoad.RecoveryTests | Features/StorageRecovery/Cases/RecoveryTests.cs, Features/ClusterReplication/Cases/ReplicaPersistenceTests.cs, Features/ClusterReplication/Cases/ReplicaProcessRecoveryTests.cs | StorageRecovery, ClusterReplication; authored actual child-process interruption/restart; current source qualification pending. |
| tests/KeyLoad.CrashHost | Program.cs, Features/ClusterReplication/Helpers/ReplicaCrashScenario.cs | Shared real-process recovery harness, never a product replacement. |
| tests/KeyLoad.IntegrationTests | ClusterFixture.cs, Features/ClusterReplication/Cases/ClusterTests.cs, Features/ResourceExecution/Cases/AdmissionClusterTests.cs | ClusterReplication and ClientApi shared fixtures; business cases mirror their owning canonical slices with real RF3/.NET and required MCP flows. |
| tests/KeyLoad.ComparisonTests | Features/BenchmarkComparisons/ | BenchmarkComparisons; real engine/container correctness and measurements, including the separate TimeSeries profile under ADR-050. |
| benchmarks/KeyLoad.Benchmarks | Program.cs | BenchmarkComparisons; embedded microbenchmark development, no public CI claim without CI evidence. |
| benchmarks/KeyLoad.BenchmarkScenarios | Features/BenchmarkComparisons/Benchmarks/EmbeddedBenchmarks.cs | BenchmarkComparisons; ADR-047 public fixture library for external generated consumer, source joined with a clean enabled host build; real GitHub Dry execution pending. |
| benchmarks/KeyLoad.Comparisons | Features/BenchmarkComparisons/Contracts/Contracts.cs, Features/BenchmarkComparisons/Corpus/BenchmarkDataset.cs, Features/BenchmarkComparisons/Execution/ComparisonRunner.cs; Features/BenchmarkComparisons/Targets/ | BenchmarkComparisons; public library with shared workload/oracle and official engine clients. |
| benchmarks/KeyLoad.ComparisonHost | Program.cs and Features/BenchmarkComparisons/ | BenchmarkComparisons; sole CLI composition/lifetime under ADR-043, source-joined with actual build and GitHub qualification pending. |
| site | Features/BenchmarkComparisons/index.html, bootstrap.mjs, measurement-loader.mjs; scripts/build.mjs | BenchmarkComparisons; product introduction, conceptual Three.js RF3 view and public views of qualified GitHub JSON; ADR-040 migration in progress. |
| tests/KeyLoad.SiteTests | KeyLoad.SiteTests.csproj, Features/BenchmarkComparisons/ | BenchmarkComparisons; independently buildable TUnit suite invokes actual Node modules and authentic GitHub report files. |
| .github/workflows | ci.yml, benchmarks.yml, release.yml | RepositoryGovernance, BenchmarkComparisons, ReleaseDelivery; build/tests, same-run metrics/site and immutable database release. |
| scripts/Features/BenchmarkComparisons | github-evidence-contracts/runs/proof.mjs, github-evidence.mjs | BenchmarkComparisons; ADR-040/BC028 authenticated-metadata and same-ZIP proof tooling, implementation pending. |
| docs | design/, implementation/, Features/, ADR/ | Product specification, evidence and canonical slice/decision records. |

[ADR-049](ADR/ADR-049-genuine-neo4j-harness.md) accepts genuine harness regressions
inside the existing comparison Aspire lifecycle and confirmed Neo4j cleanup
authority. Its exact acknowledgement/parser contract review, implementation and
GitHub qualification are pending; the accepted schema3/nine-engine graph remains.

```mermaid
classDiagram
    class ComparisonTests
    class Neo4jHarnessRegression
    class ComparisonRunner
    class Neo4jTarget
    class Neo4jQueryResponse
    ComparisonTests --> Neo4jHarnessRegression : existing Aspire endpoint
    Neo4jHarnessRegression --> ComparisonRunner : genuine public flow
    ComparisonRunner --> Neo4jTarget : target contract
    Neo4jTarget --> Neo4jQueryResponse : parsed acknowledgement
    Neo4jHarnessRegression --> Neo4jQueryResponse : real protocol input checks
```
| scripts | Features/RepositoryGovernance/verify.mjs | RepositoryGovernance; real-repository static verification with Node built-ins. |
| src/KeyLoad.Analyzers | KeyLoad.Analyzers.csproj, Features/CodeQuality/ | CodeQuality; source-owned Roslyn diagnostics attached to compilation, never runtime storage/routing. |
| tests/KeyLoad.Analyzers.Tests | KeyLoad.Analyzers.Tests.csproj, Features/CodeQuality/ | CodeQuality; real Roslyn/Orleans metadata regressions, TUnit and CI-only execution. |

## Feature convention and migration

Each vertical slice groups actual responsibilities in populated local role folders;
`Features/<SliceName>/` is an ownership boundary, not a flat file collection.
KeyLoad.Orleans now separates routing grains, commands, queries, models, contracts,
streaming, identity, serialization, diagnostics and topology. Its replication slice
separates grain services, discovery, transport, authentication, replay and wire models;
ResourceExecution separates cache-control models, contracts, serialization,
authentication and validation. AdminDashboard and BlobStorage own query capabilities
in `Queries/`. The migration preserves namespaces, aliases/Ids and runtime behavior.
The owner contract and checks are in [RepositoryGovernance](Features/RepositoryGovernance.md)
and [ADR-032](ADR/ADR-032-mcaf-governance.md).

Canonical slice names use PascalCase consistently: RepositoryGovernance, BenchmarkComparisons, DocumentStorage, RelationalStorage, EventStreams, Messaging, GraphTraversal, TimeSeries, Search, QueryExecution, Authorization, ChangeFeeds, StorageRecovery, ClusterReplication, ClusterRouting, ClientApi, BackupRestore, BlobStorage, ResourceExecution, CodeQuality and TestInfrastructure. Their owning contracts and navigation are in the [Feature index](README.md).

The embedded microbenchmark boundary follows Accepted ADR-047; it is peripheral
runner qualification and does not replace the first product server's RF3 topology.
The real generated child requires an externally visible unsealed library fixture:

[ADR-071](ADR/ADR-071-canonical-zonetree-providers.md) fixes ZoneTree storage
and ZoneTree.FullTextSearch derived text indexes. Candidate engine code and
discarded evaluation plans are removed; native WAL, node-local ownership, RF3
and all correctness/resource/qualification contracts remain mandatory.
The independent [ScaledWorkloads](Features/BenchmarkComparisons/ScaledWorkloads.md)
under [ADR-069](ADR/ADR-069-representative-scaled-workloads.md) adds actual
100K/1M/5M full-keyspace read datasets,5M calls per measurement and explicit
resource/value/provider gates. Mandatory public index/complex/RF3 stages remain
open; existing4096/270 control and site schemas remain separate.
Local ZoneTree microbenchmarks use the actual generated consumer for development.
Full native database comparisons retain isolated Linux runners and the required
resource, correctness, recovery and multi-node qualification gates.

```mermaid
classDiagram
    class BenchmarkApplication
    class BenchmarkComparisonRunner
    class EmbeddedBenchmarks
    class GeneratedBenchmarkRunner
    class ZoneTreeStore
    BenchmarkApplication --> BenchmarkComparisonRunner : dispatches arguments
    BenchmarkComparisonRunner --> EmbeddedBenchmarks : selects fixture assembly
    GeneratedBenchmarkRunner --|> EmbeddedBenchmarks : upstream generated inheritance
    EmbeddedBenchmarks --> ZoneTreeStore : owns actual embedded fixture storage
```

Target feature-owned paths are `<project-or-module>/Features/<SliceName>/`; tests mirror that name under their project roots; feature docs are `docs/Features/<SliceName>.md`. Shared entry points, composition roots, truly shared contracts and global infrastructure may remain outside feature folders. A non-applicable surface must be recorded as N/A with its reason in the feature spec.

[CodeQuality](Features/CodeQuality.md) is a compile-time slice defined by [ADR-033](ADR/ADR-033-code-quality.md). Central SDK/style analysis and source-owned Roslyn rules produce compiler/IDE findings and per-project SARIF reports. This build infrastructure has no database runtime or persistence surface.

[ADR-043](ADR/ADR-043-comparison-library-host.md) owns the preserving comparative
library/sole-CLI split. Tests retain the same public harness assembly/API and Aspire
retains its comparisons resource/configuration; source is joined but combined build
and qualification remain pending. [ADR-044](ADR/ADR-044-benchmark-immutable-contracts.md)
now accepts the separate immutable harness collection and CLR naming migration,
with valid JSON/configuration values and all real engine gates preserved.

[ADR-045](ADR/ADR-045-postgres-schema-ownership.md) accepts owned transient
PostgreSQL benchmark schemas and constant commands. Existing public target calls
retain exact workloads; typed context and a matching owner marker guard cleanup.
Real regression helpers share the existing Aspire PG container after measurement;
implementation and GitHub qualification remain pending.

```mermaid
flowchart LR
    AppHost[comparisons Aspire resource] --> ComparisonHost[Sole CLI host]
    ComparisonHost --> ComparisonLibrary[Public harness library]
    ComparisonTests[Real TUnit and engine tests] --> ComparisonLibrary
```

[ResourceExecution](Features/ResourceExecution.md) and [ADR-035](ADR/ADR-035-memory-performance.md)
define the current memory/performance repair contract. Common budgets belong to
shared node-local building blocks; operation behavior stays in its canonical
feature slice. [ClientApi](Features/ClientApi.md) owns HTTP response streaming.
Scoped document/vector visitors feed QueryExecution and Search without raw pages;
GraphTraversal retains cut-scoped visibility decisions. The shared canonical JSON
writer streams fingerprints to a hash and preserves exact signatures. The located
[repair inventory](implementation/memory-performance.md) records every open join.
Implementation and real qualification are in progress; read-only findings do not
establish measured speed, bounded process RSS or production readiness.

Shared atomic commit/outcome dispatch remains outside feature folders because it
joins multiple mutation families. Document CRUD handlers and transaction-scoped
image carriers now belong to `Core/Features/DocumentStorage/`; admission/inbox
ownership belongs to `Core/Features/ResourceExecution/` under [ADR-042](ADR/ADR-042-admission-resource-ownership.md).
The existing oversized aggregate DatabaseEngine partial type remains the explicit,
time-bounded ADR-041 handler-extraction deviation; splitting files alone does not
satisfy the type limit.

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
    GraphTraversalReader --> ReadExecutionBudget : visibility and output
    JsonData --> CanonicalJsonWriter : exact canonical bytes
    CanonicalJsonWriter --> CanonicalHashStream : incremental fingerprint
    ReadExecutionBudget --> IKeyValueView : bounded work
    ZoneTreeStore ..|> IKeyValueView : gate scoped implementation
    ZoneTreeStore --> ZoneTreeStoreRuntime : sole node local owner
    ZoneTreeStoreRuntime --> ZoneTreeTransaction : staged atomic work
    ZoneTreeStoreRuntime --> ZoneTreeCheckpointManager : generation replacement
    ZoneTreeCheckpointManager --> ZoneTreeCheckpointReader : strict format
    ZoneTreeStoreRuntime --> ZoneTreeBackupRestore : verified local backup
```

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

The existing flat/layered feature files are migration debt, not compliant target structure. [ADR-032](ADR/ADR-032-mcaf-governance.md) records actual paths, owners, target layout, verification and removal date. New feature-owned work must use the target convention. No runtime code is moved by the governance bootstrap.

The owner's SIMD clarification maps to Search's Accepted ADR-035 validation-only
stage: portable .NET JIT intrinsic finite-value checks, unchanged metric grouping,
scalar tails and same-source hardware-disabled GitHub proof. ZoneTree remains the
node-local native storage engine; Orleans remains the routing/isolation and bounded
independent-work parallelism foundation. No Rust/FFI or unmeasured numerical/atomic
parallel reduction is introduced. [Search](Features/Search.md) and
[BenchmarkComparisons](Features/BenchmarkComparisons.md) keep source, correctness
and measured performance evidence distinct.

Public chunked blobs and partial reads have canonical source implementations under [BlobStorage](Features/BlobStorage.md) and Accepted [ADR-038](ADR/ADR-038-chunked-blob-storage.md). Official MCP and simple agent adapters belong to [ClientApi](Features/ClientApi.md) and Accepted [ADR-039](ADR/ADR-039-official-mcp-agent-api.md); business operations retain their existing Feature owners. The typed .NET blob lifecycle/range adapter and SQL CALL join are specified in [central SQL evidence](implementation/central-sql.md). Exact delivered-SHA qualification remains required; private snapshot chunks and backup archive pieces are separate storage protocols.

```mermaid
flowchart TB
    SDK[Public .NET SDK and CLI] --> Server[Server gateway and identity]
    MCP[Required official MCP SDK surface] --> Server
    Server --> Orleans[Orleans request and routing grains]
    Orleans --> Hosts[Three node-local PartitionHosts]
    Hosts --> Core[Atomic engine and ordered apply]
    Core --> Storage[ZoneTree journals and files]
    Core --> Models[Documents events queues graph search]
    Aspire[Aspire Docker lifecycle] --> Hosts
    CI[GitHub Actions qualification] --> Aspire
    CI --> JSON[Raw comparison JSON with source SHA]
    JSON --> Site[README charts and Pages site]
```

## Fresh website evidence boundary (BC028)

The separate Pages consumer uses trusted control-workflow source, authentic
successful exact-attempt comparison execution and one digest-verified archive.
Publication qualifies current trusted-main website source independently from the
separate inspected measured-source checkout; candidate validation cannot deploy.
Unrelated database-workflow failure is outside the site-only boundary. Complete
site qualification and repeated website/evidence freshness proof precede Pages.
See [BenchmarkComparisons](Features/BenchmarkComparisons.md),
[ADR-040](ADR/ADR-040-static-site-threejs-evidence.md) and publication acceptance.

```mermaid
flowchart LR
    CI[Actual comparison producer] --> ZIP[Immutable report archive]
    API[Authenticated run job artifact metadata] --> Gate[Evidence Node tooling]
    ZIP --> Gate
    Gate --> Setup[Real BCL qualification inputs]
    Setup --> Tests[Full TUnit Chrome native coverage]
    Tests --> Build[Raw JSON and publication receipt]
    Build --> Recheck[Freshness proof]
    Recheck --> Pages[Separate Pages deploy]
```

```mermaid
classDiagram
    SiteGitHubArchiveSetup --> SiteGitHubArchiveReader
    SiteGitHubArchiveReader --> SiteGitHubArchiveReceipt
    TUnitSession --> SiteCoverageSourceManifestWriter
    TUnitSession --> SiteGitHubEvidenceScope
    SiteGitHubEvidenceScope --> GitHubEvidenceCLI
    GitHubEvidenceCLI --> GitHubEvidenceRuns
    GitHubEvidenceCLI --> GitHubEvidenceProof
```

## Shared request contracts

```mermaid
sequenceDiagram
    participant Caller as SDK caller
    participant API as Public gateway
    participant Grain as Request/routing grain
    participant Host as Node-local PartitionHost
    participant Replica as Replica quorum
    Caller->>API: Typed operation and authenticated identity
    API->>Grain: Authorized bounded request
    Grain->>Host: Route to physical owner
    Host->>Replica: Ordered replicated command
    Replica-->>Host: Acknowledgement at required durability barrier
    Host-->>Grain: Outcome from node-local apply
    Grain-->>API: Public result
    API-->>Caller: Contract result
```

```mermaid
classDiagram
    class KeyLoadClient
    class CommandRequest
    class PartitionRef
    class DatabaseEngine
    class IKeyValueView
    class IAtomicTransaction
    class ClusterCoordinator
    class CommandRouterGrain
    KeyLoadClient --> CommandRequest
    CommandRequest --> PartitionRef
    CommandRouterGrain --> ClusterCoordinator
    ClusterCoordinator --> DatabaseEngine
    DatabaseEngine --> IKeyValueView
    DatabaseEngine --> IAtomicTransaction
```

## Current implementation and qualification gaps

The product design retains historical DotNext-candidate and standalone-first text beneath the current owner clarification. Current AGENTS policy supersedes those choices. The current server source composes a node-local PartitionHost with the Orleans silo, distributed grain directory, activation repartitioner and separate request grains under [ADR-036](ADR/ADR-036-orleans-foundation.md). The three-node Aspire graph is present. No current integration case forces activation migration and verifies node-local storage ownership afterward; delivered-source GitHub qualification is pending. TUnit package/test migration and persisted principal/API-key verifier, grant, feed and paused local restore behavior are source-present. Public BlobStorage and official MCP/agent contracts are Accepted and source-present, with exact delivered-SHA qualification pending. The diagrams show mandatory ownership and delivery boundaries; they do not claim every target is implemented. Current build/formatter prerequisites and their exact historical source cuts are recorded in [CodeQuality evidence](implementation/code-quality.md); full solution and delivered-SHA qualification remain open.

The successful [GitHub CI baseline 36926803549](https://github.com/managedcode/KeyLoad/actions/runs/36926803549) measured commit 9c570f8c33a7a9667507a8e1c0ca68860de3be45. It does not qualify later uncommitted work, power-loss durability, endurance or production readiness. See [implementation status](implementation/status.json), [durability audit](implementation/durability-audit.md), [comparative benchmark contract](implementation/comparative-benchmarks.md), [RepositoryGovernance](Features/RepositoryGovernance.md) and [ADR-032](ADR/ADR-032-mcaf-governance.md).

## TimeSeries comparison boundary

The Accepted [ADR-034 caller-composition continuation](ADR/ADR-034-cluster-comparisons.md)
repairs three actual RF3 endpoint bindings, authenticated Rabbit management client
ownership and PostgreSQL public event readback. The existing nine-engine,
Single/Replicated, Docker load-generator and six-profile contracts remain open;
retained failed-run raw timings do not establish a performance winner. The
[owning feature](Features/BenchmarkComparisons.md) contains exact disjoint tasks,
criteria, native evidence and integration boundaries.

[ADR-050](ADR/ADR-050-timeseries-timescale-comparison.md) adds a separate profile
using the real KeyLoad RF3 SDK, digest-pinned TimescaleDB, and
ManagedCode.TimeSeries as an explicitly in-memory aggregation library. It keeps
schema3 engine counts intact and reports each arm's storage and acknowledgement
guarantees separately. Implementation and exact-SHA CI proof are pending.

```mermaid
flowchart LR
    Corpus[Deterministic UTC samples] --> RF3[KeyLoad RF3 via SDK]
    Corpus --> Timescale[TimescaleDB via Npgsql]
    Corpus --> MCTS[ManagedCode.TimeSeries memory aggregation]
    RF3 --> Oracle[Separate result and guarantees]
    Timescale --> Oracle
    MCTS --> Oracle
```

## Isolated Linux comparison execution

[ADR-056](ADR/ADR-056-isolated-linux-comparison-cells.md) and the canonical
[BenchmarkComparisons](Features/BenchmarkComparisons.md) contract add one native
engine/node-count/scenario per independent GitHub Linux runner. The closed plan,
common corpus/options, strict worker envelope and complete aggregate are owned
by the same slice across library, host, AppHost, scripts, tests and site.
Production starts RF3; explicit benchmark startup alone enables fixed RF1/RF2.
The first intensive cohort has270 planned cells; this is a plan count, not evidence
that any measurement passed. TimeSeries keeps a distinct workload and needs the
same isolation before it joins publication.

```mermaid
flowchart LR
    Contract[Canonical isolated contract] --> Selection[ComparisonWorkerSelection]
    Selection --> Aspire[Selected native resources only]
    Aspire --> Target[One native adapter]
    Target --> Runner[One selected scenario and common oracle]
    Runner --> Envelope[Raw worker schema4]
    Envelope --> Aggregate[Complete authenticated cohort]
    Aggregate --> Site[Independent site generation]
```

```mermaid
classDiagram
    ComparisonWorkerSelection --> ComparisonOptions
    ComparisonRunner --> BenchmarkDataset
    ComparisonRunner --> IComparisonTarget
    ComparisonRunner --> ComparisonFailureDiagnostics
    ComparisonFailureDiagnostics --> IComparisonFailureDiagnostics
    KeyLoadTarget ..|> IComparisonFailureDiagnostics
    KeyLoadTarget --> KeyLoadOutboxDiagnosticLine
    IComparisonTarget --> IComparisonSession
    IsolatedComparisonReport --> IsolatedComparisonWorker
    IsolatedComparisonReport --> ComparisonReport
```

[ADR-068 stageE](ADR/ADR-068-native-benchmark-gate-repair.md) adds a private
failure-only diagnostic boundary after case/session settlement and outside the
measurement clock. The KeyLoad adapter performs one bounded authorized outbox
status read; only numeric context is logged and the original case is preserved.
It changes no limits, retention, operations or public transport, and remains
runtime-unqualified until authentic failed native originals join.

Source stages remain unqualified until exact-SHA real native correctness/fault,
intensive cohort, aggregate, site and publication gates pass.

The additive TimeSeries selection and physical-resource model below preserves
the separate270 route and productionRF3. These classes are source-present;
native dispatch, membership/ACK/copy verification and the6/30 evidence join
remain open under ADR059.

```mermaid
classDiagram
    TimeSeriesIntensiveSelection --> IsolatedTimeSeriesResourceContext
    TimeSeriesIntensiveFamilyContract --> TimeSeriesIntensiveFamilyPlan
    TimeSeriesIntensiveFamilyPlan --> TimeSeriesIntensiveSelection
    TimeSeriesIntensiveRunResult --> TimeSeriesIntensiveRunJson
    TimeSeriesIntensiveRunJson --> TimeSeriesIntensiveRunJsonValidation
    TimeSeriesIntensiveRunJson --> Utf8JsonWriter : borrows caller writer
    IsolatedTimeSeriesResourceContext --> IsolatedTimeSeriesKeyLoadResources
    IsolatedTimeSeriesResourceContext --> IsolatedTimeSeriesTimescaleResources
    IsolatedTimeSeriesKeyLoadResources --> ClusterResources
    IsolatedTimeSeriesTimescaleResources --> IsolatedPostgresBootstrap
```

[ADR-059](ADR/ADR-059-isolated-intensive-timeseries.md) specifies a separate
30-cell intensive TimeSeries family using the same canonical BenchmarkComparisons
slice. KeyLoad and TimescaleDB each have native1/2/3-node preflights and five
operation scenarios, with common4096 samples, actual persisted sequence order,
bounded output lifetime and separate latency/validation-inclusive wall throughput.
Exact target/SQL/provider packets and native pinned-image feasibility precede
their staged implementation. The historical TimeSeries and270-cell protocols
remain independent; source, measured results and publication gates stay explicit.

```mermaid
classDiagram
    TimeSeriesIntensiveSelection --> TimeSeriesIntensiveWorkload
    TimeSeriesIntensiveRunner --> ITimeSeriesIntensiveTarget
    TimeSeriesIntensiveRunner --> TimeSeriesIntensiveOracle
    ITimeSeriesIntensiveTarget <|.. KeyLoadTimeSeriesIntensiveTarget
    ITimeSeriesIntensiveTarget <|.. TimescaleTimeSeriesIntensiveTarget
    TimeSeriesIntensiveRunner --> TimeSeriesIntensiveReport
```

## Atomic WAL binary serialization

[ADR-007](ADR/ADR-007-replica-consensus-bootstrap.md) and REQ/AC-REP-051 refine
ClusterReplication checkpoint publication: one physical DurableReplicaLog owns the
protocol gate shared by term/suffix planning and metadata publication. Its
materializer borrows that gate, owns ordered apply and canonical image IO, and
drains before the log owner closes the gate. Canonical snapshot IO stays outside
the protocol gate; no storage ownership moves with Orleans activations. Source
regressions use actual ZoneTree Create/Complete and node lifecycle; repaired-SHA
native recovery/RF3/intensive qualification remains required.

```mermaid
classDiagram
    IDurableReplicaLog <|.. DurableReplicaLog
    DurableReplicaLog *-- SemaphoreSlim : owns ProtocolGate
    ReplicaMaterializer --> IDurableReplicaLog : borrows gate
    ReplicaState --> ReplicaMaterializer : protocol planning
    ReplicaSnapshotStore --> IDurableReplicaLog : publishes verified metadata
    ReplicaMaterializer --> ReplicaSnapshotStore : canonical IO under apply ownership
```

[ADR-057](ADR/ADR-057-orleans-atomic-wal.md) records the earlier WAL-only source qualified by mandatory native gates at cf630751/run37084177131. That exact source used journal3/identity4/checkpoint2 and its restricted legacy promotion contract. It does not qualify the expanded internal migration below; the whole isolated comparison cohort also retains failed preflight gates. Native raw-byte ZoneTree WAL and node-local physical ownership stay under their existing owners.

The staged [ADR-058](ADR/ADR-058-orleans-coordinated-cache-memory.md) keeps RAM
acceleration under the canonical ResourceExecution slice across shared contracts,
Core, provider, Orleans/Server composition and tests. The common retained pool
and explicitly configured embedded positive point-value cache are implemented
in source, with coherence fences and real-file regression cases. Server RF3
composition remains cold while the authenticated cluster-control contract and
implementation remain open. ADR-058 R69 approves only a local opened-store
factory, exact accepted-receipt validation and finite binding prerequisites.
R81 adds a zero-wait actual canonical owner/health observation returning only
NodeId/incarnation/control RuntimeId. It cannot certify full silo readiness or a
lease; native poison and physical storage ownership remain authoritative.
The accepted [R82 v1 contract](Features/ResourceExecution/CacheControlV1.md)
adds only unused immutable generated metadata and bounded shape/transcript/
authentication/correlation primitives under Orleans/Features/ResourceExecution.
Dedicated bytes and exact original-request binding preserve existing database
and replica formats; final integrated source and native qualification are pending.
Positive point-value caches remain disposable provider state; logical policy grains and actual
per-silo services must authenticate physical host IDs and leases before cluster
admission. They never move files/handles or replace current authorization/read
barriers. Cluster-control source, native cache correctness, coverage and matched
performance qualification remain open; development builds do not close those gates.

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
    class ZoneTreePointCacheControl {
        TryReadOwnerIdentity(identity)
        TryApply(receipt)
        Retire(withdrawnRevision)
        CloseAdmission()
    }
    ZoneTreeStore --> ZoneTreeStoreRuntime
    ZoneTreeStoreRuntime --> ZoneTreePointCacheLifecycle
    ZoneTreePointCacheLifecycle --> ZoneTreePointCacheControl
    ZoneTreePointCacheControl --> ZoneTreePointCacheControlState
    ZoneTreePointCacheControl --> ZoneTreePointCacheOwnerObservation
    ZoneTreePointCacheOwnerObservation --> ZoneTreePointCacheOwnerIdentity
    ZoneTreePointCacheOwnerObservation --> ZoneTreeStoreRuntime : actual reader and health
    ZoneTreePointCacheControlState --> ICacheReadPermit
    ZoneTreePointCacheControlState --> ZoneTreePointCacheBinding
    ZoneTreePointCacheBinding --> ZoneTreePointCache
    ZoneTreePointCache --> ICacheMemoryBudget
```

```mermaid
classDiagram
    class CacheControlWire {
        TryEncodeSigned(message)
        TryEncodeForSigning(message)
    }
    class CacheControlAuthenticator {
        TrySign(message)
        TryAuthenticate(message)
        Dispose()
    }
    class CacheControlCorrelation {
        TryCreate(request)
        TryMatch(request, reply)
    }
    CacheControlWire --> CacheControlShape : complete preflight
    CacheControlWire --> CacheControlDigest : canonical raw bytes
    CacheControlAuthenticator --> CacheControlWire : dedicated transcripts
    CacheControlAuthenticator --> CacheReadyProof : nested authentication
    CacheControlCorrelation --> CacheControlHeaderComparison : exact echo
    CacheControlCorrelation --> CachePhysicalBinding : own physical target
    CacheGrantRequest --> CacheReadyProof : fixed three slots
    CacheGrantReply --> CacheReplyCorrelation : original signed request digest
```

These unused wire helpers have no receiver or server caller. The class map records
source ownership; it does not establish a lease, RF3 cache admission or performance.

```mermaid
classDiagram
    ZoneTreeTransaction --> ZoneTreeJournalCodec : prepares binary payload
    ZoneTreeJournalPublication --> ZoneTreeJournalCodec : validated payload
    ZoneTreeJournalRecovery --> ZoneTreeJournalCodec : complete checked decode
    ZoneTreeJournalCodec --> ZoneTreeJournalMutation : stable generated fields
    ZoneTreeStoreInitializer --> ZoneTreeIdentityFile : format4 writer fence
```

The isolated CI cell setup/teardown are colocated executable infrastructure under
`.github/workflows/Features/BenchmarkComparisons/`, each with local policy.
They import the common native image and retain authenticated job/image evidence;
only the caller's real TUnit cell runs database operations. Complete raw workers
stay in immutable GitHub artifacts; Pages receives a bounded derived projection
and exact original manifest after all270 raw reports validate.

```mermaid
flowchart LR
    Build[Source-bound native image roundtrip] --> Preflight[27 separate Linux jobs]
    Preflight --> CRUD[108 separate CRUD jobs]
    CRUD --> Specialized[162 separate specialized jobs]
    Specialized --> Capture[Authenticated jobs and artifacts]
    Capture --> Raw[Complete raw aggregate]
    Raw --> Compact[Bounded derived website projection]
```

## Native internal serialization

[ADR-060](ADR/ADR-060-native-internal-serialization.md) and
[InternalSerialization](Features/InternalSerialization.md) require native generated
Orleans payloads with stable aliases and field IDs across owned persistence,
replication, grain requests/replies, membership and signed claims. The owner-directed resumption installs the reviewed native source after rebasing
against current work. Shared collection/reference/depth/graph/DOM validation and
missing-identity/backup-cut regressions are being integrated before exact-source
GitHub qualification. Historical isolated compilation and WAL-only qualification
do not establish expanded runtime, fault or performance results.

Public HTTP/MCP JSON, exact user content, frozen canonical fingerprint identities,
sortable keys, raw ZoneTree values, fixed checksum/HMAC framing and blob bytes
retain their concrete protocols. Native generated bytes are not canonical hashes
for unordered values. Current data epoch6/journal4/checkpoint4/backup2,
homogeneous-v2 native payloads and KLT2/replica2 keep explicit format fences.
Ordinary readers do not accept earlier epochs; Compact preserves opaque records
and cannot convert them. [ADR-077](ADR/ADR-077-offline-native-data-epoch.md)
defines the explicit stopped native5/journal4/checkpoint3 per-store copy adapter
and the complete-node extension. The per-store source has local process-recovery
evidence; complete-node source and real-prior component/crash oracles are being
integrated. Genuine prior-to-current Linux Docker/Aspire RF3 rollout qualification
remains pending. Every voter stays stopped through preparation and publication;
this is not a rolling-write compatibility contract.

```mermaid
flowchart LR
    Public[Public JSON protocol] --> Typed[Attributed owned DTOs]
    Typed --> Native[Cached Orleans codecs and pooled sessions]
    Native --> Records[Typed records and versioned metadata]
    Native --> Peers[Grain and replica binary payloads]
    Native --> Claims[Signed native claims]
    Records --> Files[Node local ZoneTree and atomic WAL]
    Peers --> Admission[Signature scope and bounded native inspection]
    Typed --> Identity[Frozen canonical fingerprint material]
```

```mermaid
classDiagram
    NativeSerialization --> NativePayload : versioned generated root
    NativeSerialization --> NativeSerializerProviders : cached official providers
    NativePayload --> NativeJsonValue : structural DOM surrogate
    ZoneTreeIdentityFile --> ZoneTreeMetadataBinary : explicit format fence
    ReplicaProtocolCodec --> ReplicaEntryBatch : exact encoded byte accounting
    GrainRequestEnvelope --> GrainValue : attributed native boundary
```

The complete-node operator owns all three original file locks and immutable
source inventory, prepares both canonical and replica stores, converts every
historical replica image, and changes only the published descriptor length/SHA.
Private-copy verification precedes publication; checksummed native receipts bind
retry ownership without exposing keys. Published retry preserves later current
writes. The all-three preparation/publication/start barriers belong to the
separate RF3 acceptance fixture, not an individual per-node CLI invocation.

```mermaid
flowchart LR
    Original[Stopped native5 node] --> Locks[Original node and store ownership]
    Locks --> Inputs[Private identity and WAL copies]
    Inputs --> Stores[ZoneTreeFormatUpgrade]
    Original --> Images[ZoneTreeSnapshotFormatUpgrade]
    Stores --> Plan[ReplicaSnapshotFormatUpgrade]
    Images --> Plan
    Plan --> Prepared[Bound closed node stage]
    Prepared --> Verify[Private current verification]
    Verify --> Publish[Locked node publication]
    Publish --> RF3[All three published before homogeneous Aspire start]
```

```mermaid
classDiagram
    ServerNodeFormatUpgrade --> ServerNodeUpgradeBuilder : prepares one stopped node
    ServerNodeUpgradeBuilder --> ZoneTreeFormatUpgrade : authoritative store copies
    ServerNodeUpgradeBuilder --> ZoneTreeSnapshotFormatUpgrade : bounded image conversion
    ServerNodeUpgradeBuilder --> ReplicaSnapshotFormatUpgrade : same logical pointer cut
    ServerNodeFormatUpgrade --> ServerNodeUpgradeVerifier : private copies and read only images
    ServerNodeFormatUpgrade --> ServerNodeUpgradeReceipt : immutable source and target binding
```


## Isolated read/control admission integration

[ADR-036](ADR/ADR-036-orleans-foundation.md) TASK-ISO-021 separates signed
application ReadProbe/ReadBarrier admission from trusted native membership
ControlReadBarrier and heartbeat reserve. ClusterReplication owns real consensus
rounds/strict new-method guards; Orleans ClusterReplication owns signed
classification/replay snapshots, and ClusterRouting owns the same node-owned
consensus membership read. Production RF3, request-grain isolation, persisted
authorization, ordered apply and node-local ZoneTree ownership remain required.

```mermaid
flowchart LR
  Request[Authorized request grain] --> AppRead[Application read barrier]
  AppRead --> Probe[Signed empty ReadProbe]
  Probe --> ReadPool[Bounded ReadBarrier pool]
  Membership[Native membership table] --> Control[Trusted control read]
  Control --> Heartbeat[Signed control barrier and empty Append]
  Heartbeat --> Critical[Reserved Critical pool]
  ReadPool --> Receiver[Node owned consensus and apply]
  Critical --> Receiver
  Receiver --> Store[Committed ZoneTree read cut]
```

Numeric quota/configuration logs retain actual pool evidence; they cannot alone
prove successful control execution under pressure. Native Linux1/2/3 and genuine
Docker/Aspire RF3/fault gates remain required before performance/publication.


TimeSeries intensive runner remains a distinct internal BenchmarkComparisons
surface under ADR059 TASK-ISO-TS007B. Its16 bounded loops release each decoded
response after synchronous verification and retain only50000 compact value
records; wall throughput includes validation. Real target adapters own transport
drain, the host owns namespace/resources and the original cell lifetime. The
source stage does not establish the separate6/30 native family or site evidence.


```mermaid
classDiagram
    class ITimeSeriesIntensiveTarget
    class TimeSeriesIntensiveRunner
    class TimeSeriesIntensivePhaseExecutor
    class TimeSeriesIntensiveAttemptLedger
    class TimeSeriesIntensiveRunResult
    TimeSeriesIntensiveRunner --> ITimeSeriesIntensiveTarget : borrows
    TimeSeriesIntensiveRunner --> TimeSeriesIntensivePhaseExecutor : five repetitions
    TimeSeriesIntensivePhaseExecutor --> TimeSeriesIntensiveAttemptLedger : sixteen loops
    TimeSeriesIntensiveAttemptLedger --> TimeSeriesIntensiveRunResult : compact attempts
```

## Database term-read hot path

The accepted [ClusterReplication term metadata contract](Features/ClusterReplication/ReplicaTermMetadata.md)
and [ADR-061](ADR/ADR-061-bounded-replica-term-metadata.md) retain both public
authorized quorum cuts, the fixed RF3/node-local storage boundary and every ACK,
WAL, snapshot and strict error rule. Only one physical-log scalar term observation
is reused at the same gated provider position/authority. Source discovery does
not establish the contribution of any stage to native latency.

```mermaid
flowchart LR
    SDK[Public SDK or MCP] --> Auth[Fresh authentication request grain]
    Auth --> AuthCut[Authorized RF3 quorum cut]
    AuthCut --> Credentials[Persisted credential and principal]
    Credentials --> Request[Fresh operation request grain]
    Request --> OperationCut[Authorized RF3 operation cut]
    OperationCut --> Host[Node local PartitionHost]
    Host --> Log[Physical replica log]
    Log --> Store[Actual ZoneTree read gate and health]
    Store --> Term[One scalar term observation at exact authority and cut]
```

```mermaid
classDiagram
    class PartitionHost
    class DurableReplicaLog
    class ReplicaTermObservation
    class IAtomicStore
    class ZoneTreeStore
    PartitionHost --> DurableReplicaLog : physical owner
    DurableReplicaLog --> ReplicaTermObservation : one scalar cell under monitor
    DurableReplicaLog --> IAtomicStore : gated read and durable commit
    ZoneTreeStore ..|> IAtomicStore
```

Implementation/new real-store term regressions, delivered-SHA native full gates
and matched performance/server profiles remain pending. Per-request Orleans
actors never own the cell or files. No distributed data-cache enablement is
inferred from this separate private log optimization.

## Intensive TimeSeries executable input ownership

The staged [ADR059 input contract](ADR/ADR-059-isolated-intensive-timeseries.md)
links the closed6-preflight/30-cell family plan to one native engine/group and
private host settings. AppHost owns selected resource composition, Host owns
validated input/lifetime, Comparisons owns common corpus/oracles/attempts, and
ComparisonTests owns later actual member/copy/SDK/MCP qualification. Existing
productionRF3 and270 comparison contracts remain. Input source/model readiness
does not establish native copies, completed measurements or publication.

```mermaid
flowchart LR
    Configuration[Selected family configuration] --> Plan[FamilyPlan and original contract hash]
    Plan --> Resources[AppHost native group and one runner]
    Plan --> Settings[ComparisonHost validated private settings]
    Resources --> Native[Pending actual member ACK and copy checks]
    Settings --> Native
    Native --> Raw[Original settled run JSON]
    Raw --> Cohort[Pending authenticated complete family and site]
```

```mermaid
classDiagram
    class TimeSeriesIntensiveFamilyPlan
    class IsolatedTimeSeriesBenchmarkResources
    class TimeSeriesIntensiveHostSettings
    class TimeSeriesIntensiveHostNativeSettings
    class ComparisonExecutionIdentity
    TimeSeriesIntensiveFamilyPlan --> IsolatedTimeSeriesBenchmarkResources
    TimeSeriesIntensiveFamilyPlan --> TimeSeriesIntensiveHostSettings
    TimeSeriesIntensiveHostSettings --> TimeSeriesIntensiveHostNativeSettings
    TimeSeriesIntensiveHostSettings --> ComparisonExecutionIdentity
```


SG009P adds a private real-process qualification boundary for the original-store
inspection guard (ADR-059 / REQ-STORAGE-015 / AC-SG009P-001..004). Root owns the
facade/friends/dispatch; child owns guarded original handles, parent owns existing
outer lock and original process/pipe settlement. Native benchmark control and
physical business-copy verification remain separate undelivered joins.

```mermaid
flowchart LR
    Unit[Real file TUnit fixture] --> Parent[Existing outer lock and process owner]
    Parent --> Protocol[Bounded private input]
    Protocol --> Crash[Original Release CrashHost inspector]
    Crash --> Guard[Original store guard]
    Guard --> Facts[Safe actual identity data errors]
    Facts --> Join[Actual exit and both readers]
    Join --> Reopen[Release then normal reopen]
```

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

## Delivery workflows

[ADR-064](ADR/ADR-064-three-pipeline-release-delivery.md) replaces the historical
[ADR-062](ADR/ADR-062-workflow-separation.md) placement with exactly three workflows.
`ci.yml` (`CI`) combines PR/main/manual build, format, rules, analyzers, normal/scalar
units, recovery and genuine Docker/Aspire RF3 SDK/MCP qualification. `benchmarks.yml`
(`Benchmarks`) preserves every isolated Linux native comparison and TimeSeries image
check, then runs the complete site qualification/deployment stages after successful
aggregate/image gates. Website metrics authenticate this exact run/attempt/source;
historical legacy archives keep their genuine original identity.

[ReleaseDelivery](Features/ReleaseDelivery.md) owns `release.yml` (`Release`): an
immutable UTC version reservation `vM.m.yyMMdd.N`, full build/packages, self-contained
Linux server/CLI RF3 distribution and actual versioned Docker exports. Publication
requires successful exact-source CI, verifies asset hashes, and creates immutable
tag, GHCR images and GitHub Release. Packaging does not establish runtime/endurance
qualification. Provider and exact-SHA proof remain separate from source integration.

```mermaid
flowchart LR
    Source[PR or main source] --> CI[CI build and ordinary tests]
    Main[Main source] --> Benchmarks[All isolated native benchmarks]
    Benchmarks --> Aggregate[Complete authenticated JSON]
    Aggregate --> Site[Full website tests and publication]
    Manual[Manual main release] --> Reserve[UTC date and daily number]
    Reserve --> Build[Packages database distribution images]
    CI --> Gate[Successful exact source proof]
    Build --> Publish[Immutable tag GHCR and GitHub Release]
    Gate --> Publish
```

## Awaited native search execution

Native Search scheduling follows [ADR-081](ADR/ADR-081-awaited-native-search-execution.md):
one analytical reservation spans the complete awaited default-scheduler read and
native settlement while the Orleans request grain yields. Physical handles,
committed state and authorization remain node-local/canonical. Genuine RF3
liveness qualification is required before claiming the scheduler repair delivered.

## Managed ANN computational candidate

[ADR-019](ADR/ADR-019-managed-ann.md) and [ManagedAnn](Features/Search/ManagedAnn.md)
define the immutable Query/Search computational candidate. A private builder
copies bounded canonical vector input into owned packed arrays. Each search uses
its own admitted scratch and the existing exact metric implementation. This
candidate is not connected to public search; node-local projection persistence,
source-cut replay and authorized public approximation require their separate
accepted contracts and actual qualification.

[ManagedAnnSeed](Features/Search/ManagedAnnSeed.md) freezes the R2A input boundary:
Core/Search captures persisted authority, copied full-space visible vectors and
scalar source/applied/outbox witnesses inside one node-local read cut. Its private
ordinal sort and exact-bit hash run after releasing the storage gate, under finite
owned/peak/work limits. No signing key, full document, view or store handle escapes.
This local seed is historical input, not current authorization, pinned replay or
a published ANN generation. Orleans projection coordination and public native
CQRS/identity integration require their subsequent accepted contracts and gates.
The R2A local development checkpoint passed 34/34 actual Aspire normal and scalar
cases after full Release/formatter/governance checks; source/runtime inventory
proof and retained originals are linked from the seed spec. Persistent projection,
current public authorization and delivered-source Linux/RF3 acceptance stay open.

```mermaid
classDiagram
    AnnSeedCollector --> ReadExecutionBudget : one charged canonical cut
    AnnSeedCollector --> AnnSeed : owned historical input
    AnnSeed --> PackedAnnBuilder : later explicit integration
    AnnWorkBudget --> ReadExecutionBudget : work and elapsed checks
    PackedAnnBuilder --> PackedAnnGraph : actual level offsets
    PackedAnnBuilder --> PackedAnnVectors : owned bounded blocks
    PackedAnnBuilder --> PackedAnnIndex : complete immutable state
    PackedAnnIndex --> AnnWorkBudget : per call admission
    PackedAnnIndex --> PreparedSimilarity : unchanged scores
    PackedAnnIndex --> AnnSearchResult : candidates mode and counters
```

## Independent benchmark failure publication

[ADR-080](ADR/ADR-080-benchmark-failure-isolation.md) and
[BenchmarkComparisons](Features/BenchmarkComparisons.md) preserve the full native
planned-cell inventory while allowing terminal workload failures to coexist with
successful measurements. The original GitHub job/step remains failed, its bounded
worker envelope has a null report, and the site has no numeric result for that
cell. The aggregate and site jobs explicitly wait for all matrices and run after
failures; image authority, authenticated artifacts, fairness, full website
qualification and freshness remain mandatory. KeyLoad engine repair is separate.

```mermaid
flowchart LR
  Job[Independent native Aspire cell] --> Result[Measured or failed null report]
  GitHub[Actual job steps and immutable artifact] --> Proof[Same source run attempt validation]
  Result --> Proof
  Proof --> Aggregate[Complete planned-cell aggregate]
  Aggregate --> Qualification[Site tests browser coverage freshness]
  Qualification --> Site[Values or no data and original job links]
```
