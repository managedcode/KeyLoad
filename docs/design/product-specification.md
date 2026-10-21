# KeyLoad product specification

KeyLoad is one database for AI agents. Documents, typed tables, graphs, blobs,
queues, events, search/vectors and time series coexist in one database, reference
one another through canonical identities and can use one another's data. SQL is
the familiar shared language: one bounded request reads queue messages, resolves
the referenced entities and writes knowledge-graph relationships; the reverse
request creates queued actions from the graph.
[DatabaseComposition](../Features/DatabaseComposition.md) and
[ADR-067](../ADR/ADR-067-composable-agent-database.md) define the first atomic
server stage. Logical tables, collections and queues organize one database; data
copies, canonical references, persisted authorization and atomic partition
boundaries are preserved. Full declarative SQL and a client protocol remain
mandatory; the SQL CALL stage does not complete that requirement.

**Target stack:** C# / .NET 10, Microsoft Orleans, ZoneTree, ZoneTree.FullTextSearch, Aspire.
**Models:** JSON documents and indexes, typed relational rows, property graph, time series, vectors, full-text and hybrid search, blobs, Event Store, durable queues and topics.

This specification is the engineering design. Numeric limits in the test and
query-budget sections are initial engineering targets. Nothing here is a measured
result, a prototype outcome or a qualification claim; package versions and source
commits are pinned in the central manifests.

SQL is the central versioned language for every model. Performance and SIMD
(.NET vector intrinsics) are first-priority work; acceleration claims require
comparable GitHub cluster measurements. [ADR-054](../ADR/ADR-054-central-sql.md)
and [ADR-055](../ADR/ADR-055-typed-relational-rows.md) define the first SQL
invocation and typed-row stage; JOIN, foreign-key and declarative-source stages
follow it. "One server" does not change the number of data copies or the
node-local journals.

## Cluster and execution concept

The KeyLoad cluster is Microsoft Orleans
([ADR-136](../ADR/ADR-136-orleans-partition-ownership.md)). Orleans does
all of the clustering: membership, silo discovery, failure detection, the
distributed grain directory, placement, activation migration and repartitioning,
internal communication (grain calls, Grain Service calls, Orleans streams),
timers, reminders, durable jobs and scaling. KeyLoad adds no consensus,
membership, transport, placement or movement mechanism of its own.

- **Connection grain.** Every connected client has one Orleans grain
  ([ADR-125](../ADR/ADR-125-connection-owned-execution.md)). Every operation gets
  fresh persisted authorization, bounded admission, cancellation and an
  idempotent command outcome; clients never supply trusted roles.
- **Partition grains.** A logical partition grain is an ordinary Orleans grain
  that serializes and routes the commands of one logical (atomic) partition.
- **Physical storage owner.** A silo-local Grain Service owns open ZoneTree
  stores, WAL, file locks and ordered apply. Isolated volumes use each node's
  local files and typed Orleans calls between owners. Shared volumes expose the
  same paths to participating silos and require one open owner per mutable
  physical partition, joined close before reopening and no data copying.
  [ADR-136](../ADR/ADR-136-orleans-partition-ownership.md) defines these
  contracts and their open runtime and delivered-source qualification gates.
- **Copies.** The writer finds the target silos through Orleans membership
  (`IClusterMembershipService` / `IManagementGrain`) and sends the write through
  ordinary Orleans calls to the ZoneTree owner Grain Services on
  `KeyLoad:ReplicaCount` silos. The write is acknowledged after every configured
  copy confirms persisted apply. `ReplicaCount` is positive, defaults
  to 1 and is not larger than `KeyLoad:SiloCount`; `KeyLoad:StorageMode` selects
  `IsolatedVolumes` (each silo owns its own disk, the default) or `SharedVolume`
  (one shared disk, `ReplicaCount=1`)
  ([ADR-143](../ADR/ADR-143-independent-silo-replica-topology.md)). An unfinished original command can complete on
  a missing copy through a stable-ID retry; unrelated gaps require explicit
  recovery and general empty-copy restoration remains unqualified.
- **Request flow.** Public API (HTTP / .NET SDK / MCP / SQL) -> connection grain
  -> partition grains -> ZoneTree owner on the same node or on other nodes
  through Orleans -> results are gathered -> returned.
- **Background work.** Queue deadlines, due and recurring work, remote transfers,
  ANN projection and time-series chunk workers never check leadership. Orleans
  `[StatelessWorker]` grains share a helper-created logical pool key. Orleans
  automatically creates and retires activations according to load; application
  code does not address individual activations. Each activation accepts events,
  buffers them in memory and, on an Orleans timer whose
  interval is a typed option (for example 5 seconds), write the batch through the
  normal grain -> ZoneTree owner path.
- **Identifiers.** Every grain key and every identifier for objects, counters and
  storage keys comes from one shared helper per identity kind. Keys are
  case-sensitive.

## Navigation

| Topic | Sections |
|---|---|
| Storage, transactions, cluster, recovery | 1–15, 18, 20 |
| Platform and comparable systems | 21–22 |
| SQL, JSON/C# API, planner and execution | 23–25 |
| Three-way search and algorithms | 26–27 |
| Partitions, copies, scaling, regions | 28 |
| Users, policies, sensitive fields | 29–30 |
| Performance, PostgreSQL benchmark, product scope | 31–33 |
| Solution layout and ADRs | 34, 36 |
| Event Store, work queues, topics and consumer groups | 37–40 |
| TransactionDomain, atomic processing, Orleans and scheduler | 41–42 |
| Event/queue SQL, SDK, PII, retention and restore | 43–44 |
| Eventing benchmarks and ADRs | 45–46 |

Section numbers are stable identifiers referenced by ADRs and feature specifications, so they are not renumbered.

## 1. Product contract

KeyLoad is a self-contained all-in-one data and reliable-delivery platform for .NET systems: a multi-model database, an Event Store and a durable message broker. The core model unit is an entity with its document, relationships, event stream, tasks and search representations. Modules share the catalog, permissions, transaction domains and recovery contracts.

Baseline end-to-end scenario: `Company → Project → Document → Chunk`. A document holds attributes and provenance; edges define relationships; time series store timestamped samples and metrics; the Event Store holds business facts and stream versions; queues manage task delivery; vector fields provide semantic search; the text index provides lexical search. A query may restrict results by tenant, project, graph neighborhood, time period and access rights.

### Run modes

| Mode | Composition | Purpose |
|---|---|---|
| Embedded | Storage core and models in the application process | Local tools, tests, small applications |
| Single node | One Orleans silo and one copy (`KeyLoad:SiloCount=1`, `KeyLoad:ReplicaCount=1`), local data directory | Development and single-node configurations without copy fault tolerance |
| Cluster | `KeyLoad:SiloCount` Orleans silos, `KeyLoad:ReplicaCount` data copies (default 1, configurable up to the silo count), a ZoneTree owner on every silo | Main server topology, scaling and fault tolerance |

External PostgreSQL, Redis, Elasticsearch, Kafka or a separate vector server are not required dependencies. PostgreSQL is used as a benchmark competitor. Optional embedded libraries may keep their own files in the KeyLoad directory. The base configuration is managed-first; a managed ANN implementation is qualified. USearch, with a C++ core and a C# binding, stays an optional research baseline. Transitive/dependencies are checked separately. [S11][S12]

### First release scope

The first release covers documents, strict indexes, append-only streams with expected revision, durable enqueue/claim/ACK with basic retry, an atomic document + event + queue-lane batch, persisted idempotency, scalar SQL, the JSON/C# API, permissions/PII omission and offline backup/restore. Before use with real data, all eventing outputs pass security gates. The server topology is an Orleans cluster with three copies by default; ordinary Orleans calls perform the copying (see Section 6).

### Out of scope

Separate projects: full PostgreSQL SQL/wire compatibility, Cypher/SPARQL compatibility, global serializable transactions, global unique constraints across arbitrary partitions, active-active multi-region writes, OLAP with large joins, and automatic execution of arbitrary user code. The SQL subset and versioned graph/search extensions are described in Section 23.

## 2. What the sources confirm

### ZoneTree

ZoneTree provides ordered persistent key-value storage, seek/range iterators, optimistic multi-key transactions, serializers, comparers, WAL and compaction. The repository states an MIT license. This is enough to build a local storage adapter; KeyLoad designs the document model and the distributed guarantees. [S1][S2]

`AsyncCompressed` is the default WAL. The documentation describes `SyncCompressed` as having a separate tail writer with a default interval of 500 ms. `Sync` has a synchronous stream write path; full power-loss durability depends on the flush contract and the storage stack. A configuration named Sync is not by itself proof of a durable ACK. [S3]

The built-in live backup covers non-transactional trees. For transactional trees the documentation explicitly requires a separate strategy. KeyLoad uses a closed, consistent backup of all data, transaction-log and metadata files. [S4]

`IteratorType.Snapshot` switches the mutable segment and reads a frozen read-only cut. `NoRefresh` may see later changes in the current mutable segment. A Snapshot iterator can cause extra segment moves, so its cost is part of the benchmark. Alignment of snapshot reads with transaction visibility is verified separately. [S5]

### Orleans

Orleans provides actor identities, routing, activation lifecycle and all KeyLoad clustering: membership, silo discovery, failure detection, the distributed grain directory, placement, activation migration/repartitioning, internal calls and scaling. The default grain directory tolerates short-lived duplicate activations during instability; the distributed directory is an experimental/preview capability in the current documentation. Durable data state therefore belongs to the silo-local ZoneTree owner, not to the directory or an activation. [S6][S7]

Orleans offers distributed transactions for transactional grain state; integration requires matching state storage. For KeyLoad this is a separate integration option: the mechanism is not established to cover arbitrary ZoneTree trees, ANN files or local disks automatically. [S8]

### Search

`ZoneTree.FullTextSearch` is the selected text index. It documents token/record indexing and boolean search; the reviewed README does not confirm BM25, so BM25 ranking is KeyLoad's responsibility on top of it. [S9]

USearch has a C# SDK, vector search and save/load/view over a C++ core. It stays a research baseline. The base managed-first deployment requires qualifying a separate managed ANN implementation; API, deletion, concurrency, persistence and memory ownership are verified against pinned source/package versions. [S11][S12]

RRF merges ranked results from different retrievers. It is a suitable starting scheme for KeyLoad hybrid ranking; specific weights and candidate windows are set by relevance tests. [S15]

## 3. Recommended architecture

```text
.NET SDK / HTTP API / MCP / SQL
                 │
     Connection grain (one per client)
   auth, validation, admission, limits
                 │
     ┌───────────┴───────────────────────┐
     │                                   │
Logical partition grains            Query execution
partition command order             planner, bounded fan-out,
                                    merge, traversal, ranking
     │                                   │
     └───────────┬───────────────────────┘
                 │  Orleans calls to the local or another node
                 │
   ZoneTree owner (Grain Service) on every silo
   own files, WAL/journals, file locks, ordered apply
   ReplicaCount copies; ACK after persisted apply on every configured copy
                 │
       ZoneTree storage
        documents + events + queue state
        strict indexes + inbox/outbox
                 │
     ┌───────────┴───────────────────────┐
     │                                   │
Derived text index                 Derived ANN index
version + checkpoint               version + checkpoint
```

### Responsibilities

`Connection grain`: one Orleans grain per connected client ([ADR-125](../ADR/ADR-125-connection-owned-execution.md)). It runs the client's independent operations with bounded admission, cancellation and fresh persisted authorization for every operation; it stores no trusted roles, completed-request history or storage handles.

`Partition grain`: an ordinary Orleans grain that serializes and routes the commands of one logical partition to the ZoneTree owners on `ReplicaCount` silos. The number of grains follows the number of operational partitions. A per-document grain may be an optional facade; the base data path works with partition batches. The base architecture creates no grain per edge, sample or ANN node. Orleans may migrate a partition grain; migration does not move files.

`ZoneTree owner`: a silo-local physical owner exposed through an Orleans Grain
Service. It holds engine instances, file handles, WAL/journals, locks, maintenance,
reader leases and ordered apply. Scoped local services borrow its exact session
through DI. Isolated volumes open on silo initialization. Shared volumes open
lazily for an ordinary database activation and close only after that activation's
original work and scoped borrowers join. No handle crosses RPC; another silo can
reopen the shared files after actual release. Deactivation never deletes data.

`QueryCoordinator` plans a query, bounds fan-out, collects top-k and manages the overall budget. CPU-heavy traversal, scoring, vector distance and compaction run in separate bounded execution pools. No external API calls run inside long grain turns or write gates.

`CatalogService` stores collection/event/message schemas, TransactionDomain bindings, queue/subscription configuration, index generations and format versions as canonical database records; they are copied to the ZoneTree owners by the same path as other data.

Orleans `[StatelessWorker]` grains run interchangeable leaf work and buffered pre-aggregation. Orleans creates and retires activations automatically for a logical pool key; individual activations are not addressable. Each activation may buffer events and flush on its typed grain timer through the normal storage path. Multi-stage sessions and response streams belong to ordinary identities or actual storage owners. An in-memory buffer does not supply durable delivery. Nothing checks leadership.

One shared helper per identity kind creates every grain key and every object, counter and storage-key identifier; keys are case-sensitive.

### Cluster startup

Every node starts as an Orleans silo with `KeyLoad:SiloCount`,
`KeyLoad:ReplicaCount`, `KeyLoad:StorageMode`, its configured data path and the
clustering provider configured by Aspire. Orleans owns membership,
discovery, failure detection, directory and placement. Isolated owners recover
their files during silo initialization. Shared storage opens only at its
ordinary database owner; other API nodes can route there without opening files.
Database readiness checks the canonical owner through Orleans. Scaling adds
silos; startup copy settings describe independent persisted stores. Shared mode
requires one copy because all participating silos reach the same physical files.

## 4. Partition model and physical storage

Entity identity: `(TenantId, CollectionId, PartitionKey, EntityId)`. The SDK must pass the partition key or have an explicit routing lookup. Lookup by an arbitrary EntityId without routing information has a separately defined cost. AtomicPartitionId denotes an immutable transaction scope and is the identity of the logical partition grain; the ZoneTree owner of every silo holding a copy stores the files (see Section 28). Shared domain resolution for documents, stream sets and queue lanes is described in Section 41.1. The parent database scope is part of the full resource identity.

Default affinity: tenant + collection + application partition key, for example projectId. Large tenants may have many partitions. Global hash(EntityId) is worth evaluating for random CRUD; project/graph affinity reduces cross-node hops. The locality-versus-hotspot trade-off is assessed per workload.

Logical partitions and physical ZoneTree instances are separate levels; one node's ZoneTree owner stores many partitions. The number of ZoneTree instances is chosen by measurement. Every open engine consumes RAM, cache, handles and compaction resources, so fixing thousands of separate trees before benchmarking is not justified.

### Namespaces

Below is the logical key layout. On disk, keys use a versioned binary encoding. Because one node's ZoneTree owner stores many atomic partitions, keys carry a tenant/atomic-partition namespace. External identity always includes the full scope.

| Namespace | Key within partition | Value |
|---|---|---|
| Document | `D / collection / id` | Envelope + immutable UTF-8 JSON |
| Scalar index | `I / indexId / typedValues / id` | Revision or covering fields |
| Unique index | `U / indexId / typedValues` | Entity reference |
| Edge record | `E / graphId / edgeId` | From, to, label, attrs, revision |
| Outgoing adjacency | `EO / graphId / from / label / edgeId` | Target + edge revision |
| Incoming adjacency | `EI / graphId / to / label / edgeId` | Source + edge revision |
| Time sample | `T / seriesId / timeBucket / timestamp / sequence` | Value + event identity |
| Canonical vector | `V / collection / fieldId / id` | Model id, dimensions, metric, bytes, revision |
| Projection outbox | `O / sequence / ordinal` | Committed projection mutation |
| Deduplication | `C / clientScope / commandId` | Payload hash, outcome, revision/token |
| Apply progress | `M / lastApplied` | Committed command position |
| Event stream | `SH / streamRef`, `EV / streamRef / revision`, `ED / streamRef / eventId` | Head, immutable events and dedup |
| Event feed / snapshots | `EF / eventSequence / ordinal`, `ES / streamRef / revision / reducerVersion` | Replay positions and aggregate snapshots |
| Queue | `QB / queue / messageId`, `QM / queue / messageId` | Immutable body and mutable delivery state |
| Queue indexes | `QR`, `QS`, `QL`, `QD` | Ready, scheduled, lease expiry, dead-letter references |
| Subscriptions / inbox | `SG`, `SC`, `SD`, `IN` | Config, checkpoints/gaps, deliveries, processing receipts |

A document, its strict indexes, canonical vector and outbox event live in one transactional scope. The catalog may bind domain events and a queue lane to the same AtomicPartitionId; producer/consumer atomic batches are described in Section 41. The full event/queue keyspace is in Section 39.2. Physically separating time buckets or projections creates separate storage scopes; the API must not hide this boundary.

### Envelope

Required fields: `FormatVersion`, `Revision`, `CreatedAtUtc`, `UpdatedAtUtc`, optional `ExpiresAtUtc`, and payload length and checksum where the recovery/backup protocol needs them. Embeddings are stored in separate typed vector fields. A plain `GetDocument` returns JSON and metadata; loading large vectors is an explicit operation.

Buffers become immutable once handed to the storage layer. An ArrayPool buffer may be returned only after the lifetime of all readers and storage owners has ended. The ownership contract outweighs removing one buffer copy without measurements.

### Ordered key encoding

KeyCodec has its own version and golden test vectors. It defines type tags, a separate encoding for missing/null, signed integer ordering, UTC time ordering, string collation, escaping and arrays. A plain string length prefix does not guarantee lexicographic order. For floats, NaN, infinity and signed zero are defined. The index API may limit numeric types to clearly defined int64/decimal/double policies.

Comparers and serializers are part of the persisted data format. There is one current format; data in an unsupported or corrupt format is rejected before any effect. [S2][S5]

## 5. Write path, reads and transactions

### Single-node apply

1. The connection grain checks auth scope, JSON, schema, size and command identity.
2. A writer queue serializes the partition's commands; the bounded queue provides backpressure.
3. A dedup lookup runs; a repeat with the same ID and a different payload ends in an explicit conflict.
4. In one transaction: all preconditions are checked, index deltas are built, canonical records change, domain events/queue state/inbox receipt are added when the command has them, the outbox event is added, and the outcome and `lastApplied` are written. A shared TransactionDomain is checked before apply.
5. Acknowledgement depends on the declared durability profile and a verified flush barrier.
6. Projection workers read only committed outbox records.

All canonical writes go through this path. A direct `Upsert` into the underlying tree would bypass these invariants.

### Read contract

A point read uses committed visibility. A strict index query must see a consistent index + document cut. `Snapshot` on plain ZoneTree and the point-read helpers in the transactional API need a separate proof of concept for transactional range queries. The reviewed `ITransactionalZoneTree` provides transactional/committed point access; reading the raw underlying tree without an adapter is not sufficient proof of snapshot isolation. [S2][S5][S17]

The safe baseline for bounded scans: a shared read/apply gate, transactions completed or rolled back before the read, and a short consistent scan. An optimized committed-snapshot adapter is introduced only after property/recovery tests. Long scans, exports and index builds use controlled checkpoints or verified immutable read views; they do not hold a grain turn for the whole export.

### Guarantee matrix

| Operation | Contract |
|---|---|
| Point get | Latest committed value from the ZoneTree owner to which the partition grain routes the read |
| CAS/Put/Delete | Atomic operation within a partition |
| Batch write | All-or-nothing within one partition |
| Strict scalar/unique index | In the same transaction as the document |
| Unique constraint | Explicit partition scope; global scope only for a single-partition collection |
| Multi-partition bulk | Per-partition outcomes, partial success possible |
| Graph edges within partition | Edge and both adjacencies change atomically |
| Cross-partition edges | Separate, explicitly asynchronous protocol; eventual reverse adjacency |
| Text/ANN | Derived projections, visibility through a watermark |
| Cross-partition query | Vector of partition cuts; no global serializable snapshot |
| AppendEvents | Atomic batch with a stream-revision precondition and persisted dedup |
| Document + events + local queue | Atomic for the same AtomicPartitionId and catalog domain binding |
| Queue receive / ACK | Persisted claim/lease and fenced ACK; at-least-once delivery under the retry/retention policy |
| CommitProcessing | Inbox + protected DB effects + ACK atomic in a shared partition |
| Remote output | Source intent committed; target enqueue/dedup separately, status OutputPending |
| Stream/topic subscription | Per-source contiguous checkpoint; cross-partition cursor vector |

### Durability profiles

`Buffered` permits loss of acknowledged work and reports zero persisted copies. `ProcessDurable` describes the local process-recovery barrier. `LocalDurable` requires separate filesystem/device flush and fault evidence before it can be advertised. `CommitReceipt.PersistedCopies` independently records the actual acknowledged copies; a profile name never implies extra copies.

The profile is set per database/partition group. Silently weakening guarantees for individual requests is forbidden. The public name Durable is allowed only after the full path is verified: data WAL, transaction log, metadata/manifest, flush ordering and recovery on the specific storage contract.

## 6. Replication and failover

### Storage access and copies through Orleans

Shared-volume operation uses one open owner for each mutable physical partition;
other silos reach that owner through Orleans and do not copy the data. Isolated
volumes keep physical ownership on their nodes. Cross-partition queries send
bounded work through Orleans to each logical partition once, collect partial
results and preserve their read cuts. These are approved contracts with open
implementation and qualification gates, as recorded in
[status.json](../implementation/status.json).

Only ordinary Orleans calls copy isolated data. A partition grain applies its partition's commands in a defined order, finds the target silos through Orleans membership (`IClusterMembershipService` / `IManagementGrain`) and sends every command to the ZoneTree owner Grain Services on `KeyLoad:ReplicaCount` silos. A write is acknowledged after durable local apply on every configured copy; for `ReplicaCount=3` all three must confirm, and `ReplicaCount=1` gives no copy fault tolerance ([ADR-143](../ADR/ADR-143-independent-silo-replica-topology.md)).

A execution `ManagedCode.Communication.Command<DatabaseCommand>` has one stable `CommandId`, server-bound `UserId`, UTC `Timestamp` and generated canonical input. State-dependent CAS and unique checks run deterministically in apply order, so every copy gets the same result. A business-operation rejection is a stored result of a committed command.

A copy confirms a write only after its ZoneTree owner's required persistence barrier. One flush on one node is not enough for `LocalDurable`. Without confirmation from every configured copy the write is not acknowledged; a client timeout means an unknown outcome, which is checked or retried with the same CommandId.

### Activations and files

Orleans placement, the distributed grain directory and activation migration/repartitioning manage only activations. Files, WAL/journals and file locks stay with their silo's ZoneTree owner; a migrated grain calls the ZoneTree owners through Orleans again. Grains that need locality use execution `[Immovable]`; the rest keep migration. Orleans does all clustering; KeyLoad adds no coordination protocol of its own.

### Recovery and catch-up

The apply position is written atomically with all changes of the command. After a restart the ZoneTree owner recovers storage, completes recovery actions for uncommitted local transactions, and checks the apply position and schema version. Until recovery completes, the node serves no normal reads or writes. Retrying the retained original command may complete one interrupted application on a missing copy. Unrelated prefix gaps explicitly return RecoveryRequired; general empty-copy restoration is not qualified.

ANN/FTS projections can speed up startup; canonical records allow them to be rebuilt. Outbox retention accounts for all projection consumers and active rebuild jobs.

## 7. Document storage and scalar indexing

Documents are stored as validated UTF-8 JSON. The format defines handling of duplicate property names, maximum nesting, numeric rules and the maximum document size. Proposal: reject duplicate names, unsupported numeric cases and documents above the configured maximum.

Index kinds: equality, ordered range, composite, unique-in-partition. Further kinds: covering, partial, multikey arrays, exists/missing, schema-specific computed fields. Dynamic indexing of all arbitrary fields can create an uncontrolled number of entries, so indexes are defined explicitly.

An update reads the old indexed values, deletes stale index keys and adds new ones. Sparse/null semantics are set in the definition. An index generation has an immutable schema/analyzer/keycodec version. A single-partition collection with a global unique constraint is not split into several partitions.

Online rebuild: consistent cut + retained committed deltas → build generation N+1 → catch up → validate → atomic catalog swap → retire N after reader leases end. If an atomic cut is not proven, the first rebuild runs in a maintenance window.

## 8. Property graph

Vertices are entity references to documents. An edge has EdgeId, From, To, Label, Attributes and Revision. A separate edge record gives direct access by EdgeId; two adjacency structures serve outgoing/incoming scans.

Operations: neighbors, labeled traversal, bounded BFS, unweighted shortest path. Traversal defines maxDepth, maxVisitedNodes, maxVisitedEdges, deadline, memory budget and cancellation. The algorithm batch-fetches neighbors for the frontier; the storage path has no per-edge RPC.

Proposed protective default limits: maxDepth=5, maxVisitedNodes=10 000, maxVisitedEdges=100 000. These values are settings and are verified under load. Exhausting the budget returns `BudgetExceeded`, or an explicit `PartialResult` on opt-in.

Cross-partition edges: a canonical owner is defined for the edge/outgoing record; reverse adjacency is updated through an idempotent outbox with revision and tombstone. The API exposes the projection consistency status. Referential checks and the delete policy (`Restrict`, `Detach`, async cascade) are set explicitly. Atomic deletion of a large graph across partitions requires a separate protocol.

## 9. Time series

The logical key contains SeriesId, time bucket, timestamp and sequence/event identity. A timestamp alone is not a unique sample identifier. Time is stored in UTC with a defined precision.

API: AppendBatch, ReadRange, ReadLatest, AggregateWindow, retention policy. Idempotency uses an EventId or command-scoped sample ordinals. Series have metadata/tags; tag cardinality and the number of active series are bounded by quota.

Samples are stored in the shared ordered engine. Chunk compression, separate time-bucket trees and rollups are adopted based on measurements. For large streams a chunked storage layout reduces per-record overhead; the chunk format has a checksum/version and a late-arrival policy.

Rollups store sum/count/min/max and their own watermark. Avg is computed from sum/count. A percentile rollup requires a mergeable sketch and a documented approximation. Late events may update open buckets or go through a correction pipeline.

TTL uses one visibility rule and expiration mutations copied like any other write. Arbitrary wall-clock delete delegates on each copy must not change canonical state on their own. Bucket deletion waits for reader leases to end; physical release of files is measured.

## 10. Vector storage and ANN

Every vector field has Dimensions, Metric, ModelId/ModelVersion and NumericRepresentation. Incompatible embeddings from different vector spaces are stored in different logical fields/index generations. The API checks dimensions and allowed numeric values.

Canonical vector bytes live next to the entity in the transactional storage namespace. The ANN structure is a derived projection. This allows rebuild, re-quantization and library replacement without losing the original vectors. Orleans `[StatelessWorker]` grains update the ANN projection: they buffer committed outbox events and on an Orleans timer write a batch through the normal grain → ZoneTree owner path, without checking leadership.

Algorithms: exact SIMD search for ground truth, small collections and selective allowlists; a managed HNSW candidate with verified layout, save/load, concurrency, delete/reinsert and filtered retrieval. USearch may be an optional external comparison; its core is not a mandatory dependency of the base configuration. Implementations and platform dependencies are qualified separately. [S11][S12][S42]

Raw float32 memory: `N × dimensions × 4 bytes`. For 1 000 000 vectors of 1536 components this is 6 144 000 000 bytes, about 5.72 GiB per copy. Three copies need about 17.17 GiB for raw vectors alone. The ANN graph, identifiers, documents, caches, segment metadata and temporary rebuild state come on top. This is a size calculation, independent of benchmark results.

Filtered search must enforce tenant/ACL/scalar/graph scope. A small candidate set uses exact scoring; for a large allowed set, filtered ANN, oversampling and candidate expansion are evaluated. Post-filtering a short global top-k may return incomplete results. A final authoritative check of document existence, revision and auth scope is mandatory.

Metrics: recall@k against exact ground truth with the same filter, p50/p95/p99, build time, RAM, disk, and update/delete/rebuild impact. The target recall@10 ≥ 0.95 applies to the approved dataset and to separate filter-selectivity cohorts; it is an acceptance target that requires measurement.

## 11. Full-text and hybrid search

The `ITextIndex` interface isolates provider files and format. The provider is ZoneTree.FullTextSearch; BM25 ranking is KeyLoad's responsibility on top of it. The index is a derived projection with a durable replay/checkpoint contract. [S9]

Full-text contract: tokenization, Unicode normalization, analyzer version, field boosts, term frequency, document length, optional positions/phrase support. Ukrainian and English each need their own test corpus. Stemming/stop words are configuration that affects the index generation.

Hybrid pipeline:

```text
authenticated tenant + filter + graph scope
                    │
             candidate planning
                    │
          ┌─────────┴─────────┐
          │                   │
     BM25 candidates      vector candidates
          │                   │
          └─────────┬─────────┘
                    │
        deduplicate + weighted RRF
                    │
       current visibility / ACL check
                    │
       optional reranker + final top-k
```

Base formula: `score(d) = Σ_i weight_i / (c + rank_i(d))`. A document absent from a given result list contributes zero. `c`, candidate windows and weights are configuration; c=60 and equal weights may be used for the first experiment. Quality is evaluated on KeyLoad's own relevance set. [S15]

In distributed search, partition-local document frequencies differ. Local scoring with an explicitly documented approximation is permitted. The stricter contract uses a consistent BM25 statistics epoch and a global candidate merge per modality before RRF. Local top-k pruning limits global recall, so candidate budgets and oversized partitions are part of the tests.

Graph-scoped search first determines the allowed graph neighborhood or passes an equivalent scope to the planner. MaxDepth and auth are checked during traversal. Final results are always checked against canonical document state. Indexes are never an independent source of permissions.

## 12. Projection consistency

One canonical transaction writes the changes and the committed outbox. A worker applies the mutation and the checkpoint idempotently. External index files need an atomic generation manifest or a replay-safe local journal that binds index state to the persisted watermark.

Document revision/tombstone prevent re-applying a stale event. The checkpoint advances along the contiguous applied prefix. If an event fails to index, the watermark does not skip it without an explicit documented error state.

`CommitToken` contains ClusterIncarnation, PartitionId and Position. A composite token for multi-partition work holds several positions. `WaitForIndex(token, timeout)` completes only after the required projections catch up, or returns an explicit timeout/failure. Reaching the watermark guarantees freshness per the contract; ANN ranking stays approximate.

Index generation swap, retry windows, outbox GC and the backup cut account for tokens. After a restore to an earlier snapshot an old token may be unreachable; the API returns `TokenInvalidated` according to the cluster incarnation.

## 13. Query API

Public query frontends: the KeyLoad SQL subset (T-SQL syntax parsed by Microsoft.SqlServer.TransactSql.ScriptDom), the JSON envelope and the C# builder. They lower into one shared typed query AST. The server performs binding, field-use authorization, capability checks and planning. `IQueryable` with arbitrary client code is not part of the network contract. The full dialect and envelope are described in Section 23 and Section 24.

Proposed scalar request (`languageVersion` 1); the public schema is not yet implemented. The hybrid envelope is in Section 24.1.

```json
{
  "languageVersion": 1,
  "collection": "documents",
  "select": [
    "id",
    "title",
    "updatedAt"
  ],
  "parameters": {
    "projectId": {
      "type": "string",
      "value": "project-42"
    }
  },
  "where": {
    "eq": [
      {
        "field": "projectId"
      },
      {
        "parameter": "projectId"
      }
    ]
  },
  "orderBy": [
    {
      "field": "updatedAt",
      "direction": "desc"
    },
    {
      "field": "id",
      "direction": "asc"
    }
  ],
  "limit": 50,
  "options": {
    "deadlineMs": 2000,
    "allowPartial": false
  }
}
```

Tenant and principal come from the trusted authentication context. A vector query carries a vector attachment in the same transport envelope, with dimensions/vectorSpaceId and a permission check. The SQL, JSON and C# frontends share query options.

API per capability manifest: Put/Get/Patch/Delete, Batch, Query/Explain, Create/Build/DropIndex, Add/RemoveEdge, Traverse, Append/ReadTimeSeries, UpsertVector, Search, AppendEvents/ReadStream, Enqueue/ReceiveBatch/Ack/Nack/ExtendLease, CommitProcessing, Subscriptions/Checkpoints/Replay, DeadLetters/Redrive, GetCommandOutcome, WaitForIndex, Backup/Restore, Health/Diagnostics. The eventing grammar and SDK are described in Section 43.

A response contains results, revision, watermark/token, the index generations used, warnings and an explicit partial/error state. Pagination uses an opaque cursor with sort keys, a tie-break on the full EntityRef, schema version, expiry and integrity protection. Keeping a live snapshot between pages has its own TTL and resource quotas.

## 14. Security, operations and budgets

Tenant isolation starts in KeyCodec, routing and query planning. Transport authentication, authorization for collection/admin operations, bounded payloads, quotas and audit are required before use with real data. Orleans carries all internal calls between silos.

Request limits: document/request size, number of batch operations, fan-out, scanned records, top-k, vector dimensions, graph traversal, query memory, deadline. Reaching a limit yields an explicit outcome. An expensive full scan requires explicit opt-in or an administrative policy.

Node budgets: total managed/memory, WAL backlog, projection lag, compaction work, free disk headroom, open files, pending write queue, CPU concurrency. Admission control engages before OOM or a full disk. Every maintainer registers with the node-level scheduler.

Observability: end-to-end p50/p95/p99, allocs/op, GC pause, CPU, RSS/memory, bytes read/written, compaction amplification, copy lag, apply lag, projection lag, retry/conflict rate, recovery time and corruption indicators. By default, logs must not contain document/event/message payloads, raw sensitive headers or secrets. ACK/renew and delivery have resource reservations; messaging quotas complement query limits.

Offline backup: stop admission → drain transactions/jobs → flush → close engine/maintainer → copy all authoritative files → manifest/checksum → reopen. Restore is tested into a clean directory, verifying canonical records and index invariants. A replicated cluster is backed up by the same local backup on each silo; a globally consistent cut across silos is not part of the current design.

## 15. Performance and correctness matrix

Public vendor benchmarks serve as a reference for reproducing their methodology. KeyLoad measurements must include JSON, indexes, network, flush and replication; an isolated Upsert figure cannot replace them. [S1][S3][S16]

| Dimension | Starting set |
|---|---|
| Documents | 1 / 4 / 16 KiB, 0 / 3 / 10 indexes |
| Dataset | 100k / 1M; 10M or larger than RAM for the given machine |
| Writes | Insert, overwrite, CAS, delete, batches 1 / 10 / 100 |
| Distribution | Uniform and hot-partition/skewed |
| Reads | Point, selective/nonselective ranges, concurrent snapshots |
| Vectors | 384 / 768 / 1536 dimensions, updates and deletes |
| Vector filters | 100% / 10% / 1% / 0.1% allowed candidates |
| Graph | Fan-out 10 / 100 / 10k, cycles, disconnected vertices |
| Time series | Ordered and late arrivals, high tag cardinality, retention |
| Storage | Warm/cold cache, datasets larger than RAM, active compaction |
| Deployment | Embedded, single-node server, 3-node cluster with three copies |
| Failures | Process kill, power/reset simulation, disk full, partial I/O, corruption, partitions |

Correctness oracle: simple reference models for document/index/graph/time series, exact vector search, deterministic command history, and a model-based CAS/linearizability checker. Random tests retain the seed and the minimal failing history.

`kill -9` tests a process crash. Power-loss durability requirements need a separate fault model with loss of volatile OS/device state. Three processes on one laptop give functional cluster tests; physically independent failure domains are needed to verify operational assumptions.

## 18. Solution organization

The [architecture map](../Architecture.md) is the current map of projects, slices and boundaries. Runtime projects in `src/`: Abstractions, Core, Query, Clustering, Replication, Storage.ZoneTree, Server, Client, Cli and Diagnostics. Compiler rules live in `analyzers/`; the Aspire AppHost and shared build targets in `build/`; tests and benchmarks in their own roots. Every C# project places slices as `<project>/<SliceName>/<Role>/`.

Storage interfaces describe the required guarantees, ownership and lifetimes. They do not expose ZoneTree types in the public SDK. A provider capability carries defined semantics; running with weaker guarantees requires explicit configuration.

### Base ADRs

ADR-001: partition identity and affinity. ADR-002: command format and idempotency. ADR-003: durability/ACK barrier. ADR-004: transactional read views. ADR-005: canonical namespaces/keycodec. ADR-006: synchronous/derived indexes. ADR-008: backup and log retention. ADR-009: text/vector providers. ADR-010: query budgets/security. ADR-011: current storage format. ADR-012..ADR-022 are listed in Section 36; the eventing ADR-023..ADR-031 in Section 46.

## 20. Final recommendations

ZoneTree is the canonical storage for every KeyLoad model, and ZoneTree.FullTextSearch is the selected text index. Orleans provides the cluster and the data copies (Section 6). Producer and consumer flows share a TransactionDomain. Graph and time-series data use canonical namespaces and shared budgets. Search uses a transactional outbox, exact ground truth and rebuildable index generations.

KeyLoad's product hypothesis: shared API, routing and security, and one self-contained deployment for documents, relationships, the Event Store, queues and search; co-located atomic processing; and transparent freshness and delivery contracts. Benchmark gates must prove the performance and operational advantage.


## 21. Platform and components

### 21.1. Platform and Orleans

`.NET` is the runtime and library platform for C# applications. `Microsoft Orleans` adds virtual actors, grain addressing, messaging, activation lifecycle and all of KeyLoad's clustering. [S18][S45]

The idea of "Erlang hosting the clients" is carried forward here with Microsoft Orleans. Erlang/BEAM is a separate technology and is not part of KeyLoad's target stack. ASP.NET Core accepts network connections; Orleans runs the internal actor operations.

KeyLoad's base configuration is managed-first: storage, query planner, permissions, graph and search logic are in C#/.NET. For ANN, a managed implementation that passes separate quality and concurrency checks takes priority. USearch, with its C++ core, stays in the research matrix as an external reference. Shipping a index/ML library in the distribution requires a separate decision. Transitive dependencies and compression providers are reviewed too; managed-first does not mean every package has already been audited. [S11][S12]

### 21.2. What we build on

The server consists of ASP.NET Core, Orleans, the silo-local ZoneTree owner, KeyLoad's own planner/executor, a local policy catalog and a bounded resource governor. Scalar SQL, graph, time-series and search operators all run on this same executor.

KeyLoad-owned parts: the identity model, the atomic partition contract, query semantics, the security binder, deterministic command apply, partition routing, projection lifecycle and operational guarantees. Reused components: Microsoft.SqlServer.TransactSql.ScriptDom for T-SQL syntax and ZoneTree.FullTextSearch as the text index, with KeyLoad computing BM25 ranking on top of it. A managed HNSW implementation is a reuse candidate. Each reused component gets capability tests; the availability of a NuGet package does not close acceptance of its integration.

The first-release network API is HTTP with JSON request/response and streamed result pages. Binary vector payloads and gRPC require a transport benchmark first. One `.NET SDK` hides the transport and retries write commands with the same CommandId.

## 22. Modern systems to study on our own scenarios

The table lists relevant architectural examples from official sources. It describes specific mechanisms; judging performance requires our own benchmark.

| System | Confirmed mechanism | What to check for KeyLoad |
|---|---|---|
| PostgreSQL + pgvector | Scalar indexes, JSON-oriented indexing, full-text; ANN with iterative scans and filter/partition strategies. [S36][S37][S38] | Primary competitive baseline: query plans, CPU, RAM, latency, recovery |
| Qdrant | Query API with prefetch, multiple retrieval stages, RRF/DBSF; shared and dedicated tenant placement. [S22][S25] | Candidate planning, filtering, multi-stage retrieval |
| Weaviate | Inverted-index allowlist, filtered HNSW, ACORN strategy. [S23] | Combining scalar/ACL filters with ANN without a sharp recall drop |
| RavenDB | SQL-like RQL, vector search, document-associated time series. [S27][S28][S29] | .NET SDK usability, sessions, indexes, diagnostics and the end-to-end document model |
| SurrealDB | SQL-like SurrealQL, graph-oriented queries, fusion search functions. [S30][S31] | A multi-model query language users can understand |
| ArangoDB | AQL traversals and vector index functions. [S32][S33] | Graph locality, traversal pruning, filter-placement semantics |
| Vespa | Data-local retrieval and multiple ranking phases with global reranking. [S24] | Distributing cheap and expensive search stages across nodes |
| Neo4j | Cypher SEARCH for vector indexes inside graph queries; the documentation distinguishes index filtering from a subsequent WHERE. [S34] | Graph-scoped ANN, explaining the candidate scope and traversal cost |
| Marten + Wolverine | PostgreSQL documents/events and transactional messaging integration. [S54][S55] | Combined document/event/outbox/inbox baseline |
| KurrentDB | Stream expected revision, atomic appends and persistent subscriptions. [S48][S49] | Append/replay/consumer-group semantics |
| RabbitMQ | Publisher confirms, consumer ACK and quorum queues. [S50][S51] | Specialized durable queue benchmark |

Qdrant here means the vector database, not a market-analysis "quadrant".

RavenDB belongs in the practical developer-experience comparison: for KeyLoad users in the .NET ecosystem it is the closest functional reference point. PostgreSQL remains the primary engineering baseline. Qdrant and Weaviate provide specialized vector/filter baselines; Neo4j or ArangoDB are useful for traversal workloads. A full automated matrix of all systems requires a reproducible PostgreSQL baseline first.

## 23. Query language: KeyLoad SQL and a shared server plan

### 23.1. Decision

The public text language is **KeyLoad SQL**: a defined subset of T-SQL with extensions for JSON, graph, time-series and search. The two other inputs are a JSON query envelope and a typed C# builder. All inputs translate into one internal AST and pass the same checks.

```text
KeyLoad SQL ─── parser ──────────┐
JSON query ─── schema decoder ──┼── Unbound AST
C# builder ─── wire envelope ────┘       │
                                     Binder
                          names + types + capabilities
                              + field-use permissions
                                       │
                            Authorized logical plan
                                       │
                              Cost/rule optimizer
                                       │
                              Physical query plan
                                       │
                            Data-local execution
```

For direct Get/Put/Batch the SDK uses compact commands. They pass the same security and storage contracts; the SQL parser need not be on their hot path.

SQL-like frontends have practical precedents: RavenDB RQL and SurrealQL. They show this API shape is feasible; KeyLoad's correctness and performance depend on its own semantics and executor. [S27][S30]

### 23.2. Support boundaries

| Level | Semantics |
|---|---|
| Q1, with the scalar core | SELECT, projection, aliases, typed parameters, WHERE, AND/OR/NOT, equality/range/IN, IS NULL, ORDER BY, TOP / OFFSET-FETCH, EXPLAIN |
| Q2 | Budgeted GROUP BY/aggregates; time windows; graph operators; authorized EVENTS/QUEUE_MESSAGES views |
| Q3 | SEARCH HYBRID, named retrieval branches, RRF, candidate limits, index freshness and ranking diagnostics |
| Q4 | Bounded equijoins, CTEs, prepared statements, query profiles, a meaningful set of SQL write statements |
| Separate scopes outside these levels | Full T-SQL semantics, a SQL wire protocol, ODBC/JDBC, arbitrary recursive SQL, global distributed joins and arbitrary UDFs |

A construct the parser accepts still passes a server-side capability check. An unsupported function, join or recursive query returns a diagnostic before execution. Automatically downloading a whole collection to the client for LINQ evaluation is forbidden.

SQL text compatibility, SQL semantics, the SQL wire protocol and BI-driver compatibility have separate acceptance suites. The first release claims only the explicitly implemented subset of KeyLoad SQL.

### 23.3. Parser

Microsoft.SqlServer.TransactSql.ScriptDom parses T-SQL syntax ([ADR-130](../ADR/ADR-130-microsoft-scriptdom-tsql.md)), and its syntax tree is lowered into KeyLoad's shared AST. The parser handles syntax only; KeyLoad implements name resolution, type checking, policy enforcement, planning and execution. The examples below show KeyLoad's proposed grammar.

### 23.4. Types and JSON

For indexed fields the schema declares `string`, `int64`, `decimal`, `float64`, `bool`, `timestamp`, `entityRef` or `vector`. JSON may carry extra payload fields; using them in a query is subject to bind/typecheck and scan policy. `c.address.city` resolves to a structured field path. A field whose own name contains a dot needs a quoted identifier.

`MISSING` means an absent JSON path; `NULL` means a present null value. A scalar comparison with either yields UNKNOWN; WHERE passes only TRUE. `IS NULL` checks for an explicit null, `IS MISSING` checks for absence. `COUNT(*)` counts permitted rows, `COUNT(field)` excludes null/missing. Both pass field-use authorization. These rules are part of the KeyLoad dialect, so compatibility tests must check them explicitly.

There is no implicit string-to-number cast. Mixed numeric types follow an explicit promotion policy. `TRY_CAST` returns a documented null outcome; a plain CAST returns an error without echoing sensitive input. Parameter times are normalized to UTC; the schema preserves precision. Collation and analyzer versions are part of the index generation and the plan-cache key. For equal user sort keys the executor appends the full EntityRef as the final tie-break, because equal EntityIds can exist in different partitions. The service fields _ref/_meta/$score live in a reserved namespace and cannot be overridden by document payload fields.

### 23.5. Proposed language examples

Scalar query Q1:

```sql
SELECT TOP (50) d.id, d.title, d.updatedAt
FROM documents AS d
WHERE d.projectId = @projectId
  AND d.status = 'published'
  AND d.updatedAt >= @since
ORDER BY d.updatedAt DESC, d.id;
```

For this query the planner may use the composite index `(projectId, status, updatedAt DESC, id)` if it exists and is compatible with partitioning. Tenant and principal come from the verified request context. The projectId filter grants no rights to another project.

Hybrid query Q3, **proposed KeyLoad syntax**, not yet implemented:

```sql
SELECT TOP (20) c.id, c.title, SCORE() AS score
FROM chunks AS c
WHERE c.projectId = @projectId
  AND c.status = 'published'
  AND GRAPH_REACHABLE(
        @projectRef, c._ref,
        labels => ['contains'], max_depth => 3
      )
SEARCH HYBRID (
  TEXT(c.title, c.body, query => @text),
  VECTOR(c.contentEmbedding, query => @embedding),
  GRAPH(c._ref, seeds => @seedRefs,
        labels => ['related_to'], max_depth => 2)
)
FUSE RRF (rank_constant => 60, weights => [1.0, 1.0, 0.5])
ORDER BY score DESC, c.id;
```

Here `GRAPH_REACHABLE` sets the mandatory result scope. `GRAPH` inside SEARCH produces a separate ranked list from explicit seeds. For a two-source query, omit the third branch and give two weights. The weights in the example are a starting experiment configuration.

In this dialect WHERE defines eligibility **before** retrieval top-k. The planner may reorder physical operations only while preserving this contract. TOP applies to the final output. A late candidate-only filter requires a separate explicit operator and a documented approximation.

Proposed time-series operator Q2:

```sql
SELECT TIME_BUCKET(@step, s.timestamp) AS bucket,
       AVG(s.value) AS averageValue,
       MAX(s.value) AS peakValue
FROM TIME_SERIES(@entityRef, 'latency') AS s
WHERE s.timestamp >= @from AND s.timestamp < @to
GROUP BY bucket
ORDER BY bucket;
```

`TIME_SERIES` resolves the entity/series scope and permissions before scanning. AVG over rollups carries sum/count. The step parameter has a positive duration type; the governor bounds the number of buckets.

## 24. Query envelope, C# SDK and request lifecycle

### 24.1. Single transport contract

The JSON below describes the same three-branch query as the SQL in Section 23.5. The fields show the v1 design schema; the query capability manifest fixes the exact public names.

```json
{
  "languageVersion": 1,
  "collection": "chunks",
  "select": ["id", "title", "$score"],
  "parameters": {
    "projectId": {"type": "string", "value": "project-42"},
    "projectRef": {
      "type": "entityRef",
      "value": {"collection": "projects", "partitionKey": "project-42", "id": "project-42"}
    },
    "text": {"type": "string", "value": "crash recovery"},
    "embedding": {"type": "vectorAttachment", "attachmentId": "v1"},
    "seedRefs": {
      "type": "entityRef[]",
      "value": [{"collection": "chunks", "partitionKey": "project-42", "id": "chunk-7"}]
    }
  },
  "where": {
    "and": [
      {"eq": [{"field": "projectId"}, {"parameter": "projectId"}]},
      {"eq": [{"field": "status"}, {"literal": "published"}]}
    ]
  },
  "graphScope": {
    "root": {"parameter": "projectRef"},
    "labels": ["contains"],
    "direction": "outgoing",
    "maxDepth": 3
  },
  "retrieve": [
    {"name": "lexical", "kind": "text", "fields": ["title", "body"], "query": {"parameter": "text"}, "candidateLimit": 200},
    {"name": "semantic", "kind": "vector", "field": "contentEmbedding", "query": {"parameter": "embedding"}, "candidateLimit": 200},
    {"name": "related", "kind": "graph", "seeds": {"parameter": "seedRefs"}, "labels": ["related_to"], "maxDepth": 2, "candidateLimit": 200}
  ],
  "fusion": {"kind": "weightedRrfV1", "rankConstant": 60, "weights": [1.0, 1.0, 0.5]},
  "orderBy": [{"field": "$score", "direction": "desc"}, {"field": "id", "direction": "asc"}],
  "limit": 20,
  "options": {"deadlineMs": 2000, "allowPartial": false, "freshness": "indexed"}
}
```

On the SQL path, candidate windows and resource options arrive in the same request envelope alongside the SQL text and typed parameters. With identical parameters and options, SQL and JSON must produce the same bound plan. `candidateLimit=200` and the 2000 ms deadline are examples, not a product SLO.

Tenant, principal, role grants, policy epoch, storage addresses and internal index ordinals are never accepted as trusted fields of client JSON. An EntityRef in a query lies within the authenticated tenant; cross-tenant references are checked by a separate administrative contract.

### 24.2. C# interface

Proposed SDK usage:

```csharp
// Sketch of the KeyLoad API; not yet implemented.
var result = await client.QueryAsync<SearchHit>(
    sql: queryText,
    parameters: new QueryParameters()
        .Add("projectId", projectId)
        .Add("projectRef", projectRef)
        .Add("text", searchText)
        .AddVector("embedding", embedding, vectorSpaceId)
        .Add("seedRefs", seedRefs),
    options: queryOptions,
    cancellationToken: cancellationToken);
```

The lambda builder supports a whitelist of field accesses and operators that can be serialized into the AST. Arbitrary C# delegates never run on the server. The parser/types dependency does not leak into public DTOs. The errors `UnsupportedExpression`, `PermissionDenied`, `IndexNotReady`, `BudgetExceeded`, `TokenInvalidated` and `UnknownWriteOutcome` have stable error codes.

### 24.3. Clients and grains

Each connected client has one connection grain ([ADR-125](../ADR/ADR-125-connection-owned-execution.md)) with bounded, disposable session state: subscription state and cursor references. It must not store trusted roles, completed-request history, storage handles, individual documents or unbounded client results. Disconnect or idle expiry ends the client's work and deactivates the grain.

An Orleans activation runs one turn at a time. By default a request holds a non-reentrant activation until it completes; an await alone does not open the activation to the next request. Reentrancy allows turns to interleave; it does not create CPU parallelism inside one activation. [S18][S19]

The connection grain therefore runs independent operations in bounded call-local contexts, and scheduling is chosen per method ([ADR-110](../ADR/ADR-110-orleans-execution-primitives.md)). One slow request must not delay all of that client's requests.

StatelessWorker grains use one helper-created logical key per pool. Orleans automatically creates and retires local activations with load; individual activations are not addressable. They handle interchangeable work and activation-local buffered batches flushed on timers. Storage access follows the typed grain → ZoneTree owner path. [S20]

### 24.4. Response and cursor

A response contains queryId, items, revisions, search/index watermarks, generation IDs, policy epoch, completeness, approximation flags and redaction metadata. Detailed score contributions require a separate diagnostic permission. Plans and traces contain no payload secrets.

A cursor stores opaque resume state with tie-break keys, principal/tenant binding, policy/schema/index generation, expiry and a MAC. Moving a cursor to another principal is rejected. SQL OFFSET is available only within a small declared budget. Large results use keyset pagination or a leased query cursor.

When policy changes, a cursor is re-authorized or invalidated. Every new streaming page has an authorization checkpoint. Bytes already sent to the client before a permission is revoked cannot be recalled; the revocation contract sets the boundary at the next request/page.

## 25. Planner and executor: how a query actually runs

### 25.1. Minimal internal interfaces

The internal model separates `UnboundQuery`, `BoundQuery`, `AuthorizedLogicalPlan`, `PhysicalPlan`, `QueryFragment`, `RecordBatch` and `QueryResult`. These types belong to KeyLoad and have versioned contracts.

`IQueryBinder` binds names/paths to catalog field IDs. `IQueryAuthorizer` checks field use and adds the trusted row policy. `IQueryPlanner` selects the operations. `IShardQueryExecutor` runs a fragment over a leased committed view. `IResultProjector` applies the permitted projection and masking. `IQueryGovernor` controls actual cost.

`Candidate` contains the EntityRef, document revision, branch ID, score and the source generation/position. A shard-local ordinal is valid only inside a fragment and its generation. External results and cross-shard merges use the full EntityRef.

### 25.2. Physical operators

| Operator | Purpose |
|---|---|
| PointLookup / MultiGet | Direct key access and batched reads |
| IndexSeek / IndexRangeScan | Equality, composite prefix, ordered range |
| BitmapAnd / BitmapOr / SortedIdIntersect | Intersecting permitted IDs with scalar predicates |
| ResidualFilter | Checking permitted predicates the index does not cover |
| GraphExpand / GraphSemiJoin | Batched frontier and graph-scope check |
| TextTopK | Retrieval from the text index |
| ExactVectorTopK / AnnTopK | Exact or approximate vector candidates |
| BranchMerge / RankFusion | Cross-shard branch merge and fusion |
| PartialAggregate / FinalAggregate | Local aggregates and their merge |
| FetchDocuments / SecureProject | Late materialization, ACL, masking |
| Exchange / TopKMerge | Passing bounded batches and global ordering |

The planner is rule-based: point lookup → the most selective available scalar index → remaining residual predicates → bounded sort/aggregate. Cardinality estimates, histogram/sketch statistics and alternative plans build on it. Statistics include the security-domain scope and version.

### 25.3. Security barriers in optimization

Policy operators enter the plan before optimization. AST rewriting must not move a user expression, UDF, sort or searchable field outside the authorization boundary. Schema fields have separate capabilities for output, predicates, sorting, grouping, text/vector retrieval and raw read.

Even `EXPLAIN` checks rights on collection, index and field names. Detailed cardinalities, distances, graph paths and policy details are returned only within a permitted diagnostic scope. Parameters classified `sensitive` are replaced with placeholders in all serialized plans.

### 25.4. Read views and parallel execution

Bounded scalar scans may use the read/apply gate from Section 5. This limits parallelism within one ZoneTree owner. This option is explicitly a safe baseline; a production scalability claim requires verified immutable committed views or KeyLoad's own versioned read-view implementation.

Before launching parallel branches, the coordinator obtains the catalog/policy context and read-view descriptors. A short canonical read never mixes halves of a transaction batch. Search projections may lag, so a search plan has its own freshness contract.

Each fragment runs through an Orleans call to the ZoneTree owner of a node that holds a copy of the relevant partition and has completed recovery. QueryCoordinator reads the same committed cut within the declared guarantees. A cross-shard read returns a vector of cuts. A global timestamp in the response does not turn this set into a globally serializable snapshot.

### 25.5. What multithreading means

Writes to one logical partition are ordered by its partition grain and the ZoneTree owner's ordered apply. Independent partitions apply writes in parallel. Read-only computations over stable views may run in parallel on worker pools.

The main sources of parallelism are independent client requests, independent partitions, independent text/vector branches, multiple CPU blocks of an exact-vector scan and separate background maintenance jobs. All of them draw on a shared node-level budget. Creating a Task per document, edge or vector distance is forbidden.

`Task.Run` alone does not guarantee isolation. Heavy scoring goes through an admission semaphore/queue with bounded concurrency. Control work has its own budget and priority. Code moved off the grain scheduler receives immutable inputs and works through a thread-safe storage adapter; it must not access mutable grain state.

### 25.6. Explain/Profile

`EXPLAIN` shows route pruning, index selection, estimates, the ranking profile, candidate windows and the memory budget. `PROFILE` adds actual operator counts and times. The normalized report records queue wait, read gate, scalar filtering, graph traversal, retrieval, merge, materialization and projection lag.

`EXPLAIN` does not run an expensive query. `PROFILE` runs it under the same permission and quota rules. All estimated figures are labelled as estimates. By default, raw parameter values of sensitive fields are unavailable even in debug mode.

## 26. Text + vectors + graph: shared search semantics

### 26.1. Results belong to one shared model

Text, vector and graph retrieval must return compatible entities. For chunk search, every branch returns ChunkRef. Graph nodes such as Project or Company may be intermediate vertices; their result is explicitly projected onto chunks through an authorized traversal.

Duplicate EntityRefs are removed within each branch before fusion. Several paths to the same chunk do not multiply its contribution automatically. Several embeddings of one document use an explicit aggregation policy, for example max score. After chunk search, an optional `GroupByDocument` limits chunks per document; grouping quality is evaluated separately.

### 26.2. Three graph roles

**GraphScope.** A mandatory condition: the result is reachable from the root through the given labels within the maximum depth. An allowed-ID set may be built first and retrieval then run inside it. For a large scope the planner may use an alternative semi-join strategy while keeping the declared completeness.

**GraphRetriever.** Explicit seeds produce a third candidate list. Initial score: `1/(1 + shortestHops)` with a deterministic EntityRef tie-break, a depth cap and no path-count amplification. Edge weights, Personalized PageRank and random-walk methods are separate profiles. Seeds come from the client or from a separate, explicitly described entity-resolution stage.

**GraphExpansion.** Related context, such as the document, project or dependent incidents, is gathered from the top search hits. This expansion has its own limits and output section. A document absent from the primary candidate windows can remain undiscovered; this mode has a candidate-limited recall contract.

GraphScope and GraphRetriever can be used together, as in the SQL example. GraphExpansion often suits building an AI agent's answer after retrieval.

### 26.3. Traversal semantics

Vertex/edge ACLs are checked during traversal. The safe default: traversal stops at an unauthorized vertex/edge. Passing through hidden vertices requires a separate `graph.traverseHidden` contract, outside the minimal release.

An endpoint scalar filter such as `chunk.status='published'` must not automatically discard an intermediate Project vertex that has no status field. Predicate pushdown is classified as endpoint-only, edge condition or traversal prune. The official AQL documentation describes traversal and pruning separately, which is a useful reference for semantic tests. [S33]

Cross-partition reverse adjacency has the eventual contract from Section 8. A query through such adjacency reports its watermark and completeness. There is no promise of a strongly complete graph scope before catch-up. Stronger guarantees require an additional consistent traversal-cut protocol.

### 26.4. Recommended pipeline

```text
Authenticate → current policy epoch → bind + field-use authorization
                               │
                  Tenant / row ACL / live IDs
                               │
                Scalar filter + optional GraphScope
                               │
                     Eligible endpoint set
                               │
       ┌───────────────────────┼────────────────────────┐
       │                       │                        │
  Text branch             Vector branch            Graph branch
  BM25 candidates         exact / filtered ANN     seeded traversal
       │                       │                        │
  Merge across partitions Merge across partitions  Merge across partitions
       └───────────────────────┼────────────────────────┘
                               │
                 Validate candidate revisions / ACL
                               │
                  Refill + rank each complete window
                               │
                      Weighted RRF / Top-K
                               │
                 Optional authorized reranker
                               │
              Current output policy → safe projection
```

Validating candidates before fusion means stale or unauthorized entries must not occupy ranks in the final branch lists. Unchanged vector/text payloads may update revision metadata without fully rebuilding the index graph; this path still goes through the committed outbox and checkpoint. If revalidation after materialization removes a hit, a bounded refill and ranking recomputation run. When the refill budget is exhausted, the response explicitly reports a short/partial outcome according to the request contract.

### 26.5. Weighted RRF v1

KeyLoad's own versioned profile:

`RRF(d) = Σ_i [d ∈ branch_i] × w_i / (c + rank_i(d))`.

Ranks start at 1, `c > 0`, weights are non-negative and at least one weight is positive. A missing hit in a branch contributes zero. `c=60` and the weights from the SQL example are experiment defaults. Raw BM25 scores, cosine similarities and hop counts have different scales; RankFusion works on rank positions.

Example with c=60 and weights 1,1,0.5: candidate A has ranks 1,10,2 and scores `1/61 + 1/70 + 0.5/62 ≈ 0.0387437`. Candidate B has ranks 4,2 and is absent from the graph branch: `1/64 + 1/62 ≈ 0.0317540`. This is an arithmetic demonstration of the profile, not a relevance measurement.

Current Qdrant documentation has its own rank convention, constant and weighted formula; defaults differ between systems. KeyLoad keeps `weightedRrfV1` and its own golden fixtures, so an update of a third-party provider must not silently change ranking. [S22]

Candidate windows affect the result. A document absent from every retrieved window cannot enter fusion. With finite windows the result is exact with respect to those lists. An exhaustive global RRF top-k requires broader retrieval guarantees or a bound-based algorithm with its own proof.

### 26.6. Distributed ranking

For each modality, candidate lists are collected from all relevant partitions, a single total order is applied, a global modality window is formed, and fusion runs afterwards. RRF over local per-partition ranks can change the result after a simple data reshaping, so it is not the default contract.

For exact retrieval, a local top-L from each partition suffices for the global top-L if all partitions use comparable scores, the same cut/eligibility and a deterministic tie-break. ANN adds its own approximation error. Even an exact global top-L per modality does not automatically give an exhaustive RRF guarantee beyond the candidate union.

BM25 requires a defined corpus/statistics epoch. In the strict ranking profile, term statistics are aggregated within the tenant/security corpus. Local per-partition statistics are allowed in a separate fast profile with declared approximation. RRF between text and vector does not fix incomparable text scores across partitions.

Exact statistics under arbitrary per-user ACLs can be expensive. The minimal security contract isolates statistics between tenants and explicitly defines the corpus within a tenant. Exact score noninterference under arbitrary overlapping ACLs is not claimed; that level requires separate security-domain indexes, hidden diagnostics and a dedicated audit. A shared ANN topology can also affect timing/recall.

### 26.7. Freshness, re-ranking and embeddings

`indexed` reads the available projections and returns their watermarks. `atLeastToken` waits for the contiguous applied prefix of the required branches. A token does not define a single globally atomic search snapshot and does not remove ANN approximation. During revalidation the candidate revision is checked against the current authoritative document. A stale score from a changed document is rejected or used only in a separate explicit stale-ranking profile.

Embeddings are computed by the client or an opt-in job. The request carries a typed vector and a vectorSpaceId. Transaction apply makes no external inference calls. Model upgrades create a new vector field/index generation and control the dual-write/rebuild window.

The reranker receives only authorized fields from the safe projection. Sending raw sensitive text to an external model provider requires a dedicated permission and configuration. Privacy classification extends to derived embeddings, summaries and stored snippets. Disabling output of a field that has already entered an embedding corpus requires removing/rebuilding the affected derived representations.

## 27. Indexing and search algorithms

### 27.1. Scalar indexes and sets

An ordered index has the key `(atomicPartition, indexGeneration, typedValues, EntityId)`. A composite prefix allows range seeks. Small result sets use sorted ID arrays and merge intersections. For large sets, dense bitmaps and a managed Roaring-compatible representation are evaluated. Adopting a specific library depends on a license/format/concurrency audit.

A partition-local compact ordinal can reduce ID-set memory. The ordinal↔EntityRef mapping is versioned. After a delete or rebuild, a previous ordinal must not address another document in an old cursor/bitmap. Safe options: generation-scoped ordinals or monotonic allocation with tombstones.

### 27.2. Text index

ZoneTree.FullTextSearch is the selected text index. KeyLoad provides BM25 ranking on top of it, with field boosts, explicit analyzer versions and the committed projection lifecycle. [S9]

Relevant points from the ZoneTree.FullTextSearch README: `DeleteRecord` without a secondary index may require a full scan; API cancellation may return the part of the results already collected. The adapter must mark partial outcomes correctly. Dual-tree indexing must pass recovery tests. [S9]

Pruning optimizations such as WAND/Block-Max WAND require exact scoring fixtures and conservative upper-bound tests so that pruning never drops an admissible top-k result. A hash-only token key requires verified collision semantics; a strict mode may store token bytes or a collision-verification dictionary.

### 27.3. Vector index

Exact search over C allowed vectors of dimension d does roughly Θ(C×d) arithmetic work. Layout: contiguous numeric blocks, explicit dimensions, metric, optional normalized vectors, bounded SIMD loops and a top-k heap. The read path avoids JSON parsing of embeddings and per-distance allocations.

For ANN, managed HNSW is considered: layered proximity graph, connectivity, construction-effort and search-effort parameters, a deterministic serialized format and rebuild control. HNSW is a separate index graph over vectors; the KeyLoad property graph has its own vertices/edges and business semantics. The original HNSW paper is the algorithm reference; the actual implementation must pass KeyLoad tests. [S42]

HNSW parameters are not fixed universally. Benchmarks select them by recall/latency/memory, dimensions, filter selectivity and updates. Arbitrary deletion, concurrent search/update and persisted graph recovery are release gates. Initial design: an immutable index generation with a mutable delta and tombstones; a concurrent mutable graph is accepted only after stress tests.

### 27.4. Filtered ANN

The planner estimates allowed cardinality, filter/vector correlation, candidate budget and index capabilities. A small allowed set gets exact scoring. A large set uses filtered ANN. When results are insufficient, retrieval effort/window grows up to the limit, or exact scoring runs within the allowed budget.

Technical detail: HNSW traversal sometimes has to pass through nodes that cannot be output candidates. Naively stopping navigation at every filtered-out vertex can make part of the graph unreachable. Weaviate describes allowlist-aware traversal and the ACORN strategy; the ACORN paper studies predicate-aware ANN. [S23][S43]

This is internal ANN navigation; permissions for traversing the business graph are governed separately. A shared ANN across tenants does not automatically give timing noninterference. Highly sensitive workloads use separate indexes/security domains.

### 27.5. Business graph

Adjacency blocks are grouped by source, direction and label. The BFS frontier is batched per partition and read with one Orleans call per batch. A visited set, the shortest depth and bounded predecessor state (when a path is required) are kept. Sequential dependency rounds are bounded by traversal depth; adding CPU does not remove their network latency.

High-degree nodes get segmented adjacency and continuation tokens. A supernode is never loaded fully into one List. Weighted shortest path, PPR and centrality indexes have separate contracts and performance gates; online global PageRank is not part of the simple query path.

For graph-aware ANN, NaviX is a research reference for adaptive execution with allowed IDs; no compatible production C# component has been qualified. [S44]

### 27.6. Time-series chunks

Time-window chunks with columnar timestamp/value blocks are evaluated on top of ordered sample storage. Candidate codecs: delta/delta-of-delta for timestamps, lossless XOR or typed delta encoding for numeric samples. Adopting a codec depends on KeyLoad's own workload and lossless roundtrip/property tests.

An open chunk accepts appends; sealed chunks have an immutable manifest. Late samples go into a bounded correction chunk. Background merge runs in Orleans `[StatelessWorker]` grains: they buffer chunk events and, on an Orleans timer, write a batch through the normal grain → ZoneTree owner path, without any leadership check; each merge creates a new generation. Retention and rollups work on window metadata and store the last processed position. A scalar tag index allows pruning series before samples are read.

## 28. Partitions, copies and scaling

### 28.1. Glossary of logical and physical units

| Term | Meaning |
|---|---|
| Tenant | Isolation and quota boundary of an organization |
| Affinity key | Business grouping, for example projectId |
| TransactionDomainId | Catalog binding of resources to a shared atomic scope, see Section 41.1 |
| AtomicPartitionId | Immutable logical boundary of a transactional batch and of partition-unique constraints; identity of the logical partition grain |
| Partition grain | Ordinary Orleans grain that serializes and routes the commands of one atomic partition |
| ZoneTree owner | Orleans Grain Service on every silo; owns that node's ZoneTree files, WAL/journals, file locks and ordered apply |
| Copy | A partition's data in the ZoneTree owner of one of the `KeyLoad:ReplicaCount` silos |
| Node / silo | A server process in the Orleans cluster with its own local data directory |
| Orleans activation | Live actor execution context with its own lifecycle; Orleans may migrate it without moving files |

The public batch contract stays atomic-partition-scoped even when records of many partitions physically reside in one ZoneTree owner.

### 28.2. Routing

```text
Tenant + database + catalog resource binding + partition key
                         │
          Resolve TransactionDomain → AtomicPartitionId
                         │
                Logical partition grain
                         │
       Orleans membership → ReplicaCount silos
                         │
           Orleans calls → ZoneTree owners on those silos
```

Stable hashing has a versioned algorithm and golden vectors. A runtime object's `GetHashCode()` is not a persistent routing format. Grain keys are built by the shared helper for each identity kind; keys are case-sensitive. Orleans performs activation placement and migration.

### 28.3. Affinity and hot partitions

For corporate knowledge the default affinity may be projectId. Entities that are often traversed together are colocated in one atomic partition. For a very large project the schema declares several atomic partitions up front, for example `(projectId, bucketOfDocumentId)`. All project-wide queries are then fan-out queries, while an atomic batch acts within the selected partition.

A single hot atomic partition keeps its serial write boundary after nodes are added. Changing a model's partition key requires an explicit change of the batch/unique scope; the API does not hide this boundary.

For time series, an affinity of `(tenant, seriesGroup, timeWindow)` can be declared. Reading a long interval touches several partitions. For global CRUD with random IDs, a hash scheme fits. No single routing policy is claimed for all workloads.

### 28.4. Copies and scaling

`KeyLoad:SiloCount` sets the number of Orleans silos; `KeyLoad:ReplicaCount` sets the number of data copies of each partition (positive, no larger than SiloCount, default 1). Both are read at startup; there is no live resizing ([ADR-143](../ADR/ADR-143-independent-silo-replica-topology.md)). Scaling means adding silos; Orleans performs placement, directory and activation migration.

A write is acknowledged after durable local apply on every configured copy (see Section 6). Deployment policy checks for fully independent fault domains.

### 28.5. Read scaling and fan-out

A point query with a partition key goes to one partition. Scalar equality on projectId or a time window must prune unneeded partitions. A query without a routing predicate may need all partitions; the planner reports the fan-out and checks the budget. The connection grain gathers results from several partitions within the operation budget.

The copy that serves an ANN search reports its projection watermark; the presence of a copy does not mean its search index is ready.

Search work is distributed through bounded per-partition fragments. Not every client×partition×branch combination runs at once. Heavy queries use a tenant queue, a node queue, a concurrency cap and cancellation propagation. Hedged reads, if added, require a duplicate-work budget and the same required cut.

### 28.6. Regions

A cluster deployment runs in one region across several failure domains. A remote region may receive an asynchronous disaster-recovery copy with an explicit RPO. Synchronous multi-region acknowledgement adds network round trips to the commit path. Active-active writes, conflict resolution and global unique constraints are out of scope.

## 29. Users, roles and authorization

### 29.1. Minimal security model

Users are represented by principals of two types: human and service account. Human authentication can connect to an OIDC provider; automated server access uses scoped API keys. A key has a random high-entropy secret, a server-side verifier, expiry, scopes and revocation state. Plain API secrets are never stored in logs or in a readable principal list.

Authorization hierarchy: cluster → tenant → database → collection → row → field. RBAC defines the allowed operations; bounded row policies define owner/team/project scope. Policy evaluation is a pure bounded operation over the verified principal context and authoritative metadata. Policies contain no arbitrary SQL/C#.

Admin capabilities are separated: infrastructure operations, schema/index management, security-policy management and raw-data access. Permission to check node health must not automatically grant reading sensitive values. An administrator's physical access to process memory, disk and encryption keys is outside the field-masking threat model.

### 29.2. Permissions

| Capability | Example scope |
|---|---|
| documents.read / documents.write | Database or collection; write policy additionally checks the row and fields |
| query.execute | Allowed collections and the maximum query profile |
| graph.traverse / graph.write | Graph ID, labels, direction, vertex/edge policies |
| series.read / series.append | Entity/series scope and retention limits |
| vector.search / vector.readRaw | Named vector field; a raw embedding may have its own sensitive classification |
| field.readRaw / field.use | Specific paths/classifications and allowed usage operations |
| schema.manage / index.manage | Generation creation, DDL, reindex budgets |
| data.export / changes.subscribe | Logical export/CDC with the current projection policy |
| backup.manage / backup.restore | Privileged raw artifacts and a separate target environment |
| query.diagnose / audit.read | Plan explanation, authorized metrics, audit metadata |
| events.append/read/replay | Stream sets, streams, generations and classified fields |
| queue.publish/consume/ack/renew | Queues/lanes, delivery tokens and current consumer grants |
| queue.inspect / deadletters.read/redrive | Metadata/body policy and explicit redrive |
| subscriptions.manage / scheduler.manage | Group source/filter scope, checkpoints, schedules |

The default policy is deny. Grants limit scope. An explicit deny has a defined precedence. Tenant membership comes from server-side identity mapping. The connection grain never accepts a role list or tenant authority from an arbitrary request payload.

### 29.3. Row policy

Initial expressions: tenant equality, ownerId equality, membership in a declared group/project set, and conjunction/disjunction with a bounded number of conditions. An ACL may be inline metadata or a reference to a versioned ACL record. Ownership and ACL fields are written only by a specially authorized mutation; a generic document patch does not bypass this check.

On the query path a row policy may compile into a bitmap/ID allowlist. This is an acceleration cache. Final document materialization checks the current ACL metadata version and the current principal context. A cached grant that has become stale gives no right to return a document after the defined revocation boundary.

Official PostgreSQL RLS is a useful baseline for policy semantics and default-deny testing. Its documentation separately describes exceptions for owners/superusers/BYPASSRLS; benchmark roles must work without such bypasses. [S35]

### 29.4. Permission revocation

Proposed strong contract: after a security change is acknowledged, the next new request or streaming page uses the new policy epoch. The minimal implementation reads the current policy epoch from persisted policy records, with batching/coalescing. When the check is unavailable, the confidential data path fails closed.

Permission caches are keyed by tenant, principal, membership version, policy epoch and scope. A notification-based invalidation alone does not prove immediate revocation. A lease-based optimization must define when the policy-change ACK waits for old leases to finish and which bound assumptions the system satisfies.

Row ACL mutations live in the canonical data scope. A query that uses a cached allowlist re-checks the latest row authorization for the final output. Strict graph-path revocation requires current checks during traversal and while building the authorized result. A request already in flight has a documented snapshot/page boundary; there is no promise to retract bytes already sent.

### 29.5. Isolation and abuse controls

All lookup keys, caches, cursors, jobs and outbox subscriptions include the tenant scope. A cross-tenant key lookup must end with a generic not-found/denied outcome under a fixed documented policy, without confirming that another tenant's ID exists.

Tenant quotas cover the number of collections/indexes, storage size, active series, vector dimensions/count, document size, query CPU/memory, concurrent queries, graph fan-out, bulk batch bytes and background reindex quota. The node governor applies overall limits across all tenants; one tenant cannot consume the sum of all per-query maximums up to OOM.

## 30. Sensitive/PII fields and masking

### 30.1. Minimal implementation contract

An administrator marks a field path as sensitive. By default the raw value of such a field is excluded from the response. Access to the raw field, its use in predicates and its indexing are governed by separate rules. Sensitive classification cannot be changed through an ordinary document update.

This is database-level access control and output redaction. Original values remain in authoritative storage. Encryption at rest, TLS, backups, administrative access and audit need their own configuration. The minimal release does not automatically detect all PII in arbitrary text.

SQL Server Dynamic Data Masking demonstrates an important limitation: hidden output values can be inferred through allowed predicate queries. KeyLoad therefore checks field usage before executing a query. [S40]

### 30.2. Proposed schema policy

```json
{
  "collection": "customers",
  "policyVersion": 1,
  "fieldPolicies": [
    {
      "path": "/email",
      "classification": "pii.email",
      "defaultOutput": "omit",
      "rawReadPermission": "pii.read",
      "rawUsagePermission": "pii.query",
      "textIndex": false,
      "embeddingSource": false
    },
    {
      "path": "/phone",
      "classification": "pii.phone",
      "defaultOutput": "omit",
      "rawReadPermission": "pii.read",
      "rawUsagePermission": "pii.query",
      "textIndex": false,
      "embeddingSource": false
    },
    {
      "path": "/contacts/*/email",
      "classification": "pii.email",
      "defaultOutput": "omit",
      "rawReadPermission": "pii.read",
      "rawUsagePermission": "pii.query",
      "textIndex": false,
      "embeddingSource": false
    }
  ]
}
```

Here `*` is KeyLoad's own wildcard extension of the field-policy path for array elements. It is not a standard JSON Pointer token. The parser stores the path as structured segments, checks escaping and applies the same rules to nested projections, aliases, wildcards and computed expressions. An alternative schema may define rules by field IDs; the public syntax fixes the sensitive field catalog.

JSON response without `pii.read`:

```json
{
  "id": "customer-17",
  "name": "Example Customer",
  "_meta": {
    "revision": 12,
    "redactedFields": ["/email", "/phone"]
  }
}
```

For a principal with documents.read and the matching raw-read grant, the response may include email/phone. The presence of `redactedFields` itself reveals schema information; showing it is governed by the schema visibility policy. For a hidden schema, a generic redaction indicator without paths is returned.

### 30.3. SQL output

`SELECT *` omits sensitive fields without a raw grant. An explicit `SELECT email` may return a typed null with redaction metadata if the policy allows masked selection. Its metadata distinguishes it from an actual stored null. For schema-hidden fields the binder returns a generic denied/unknown-field outcome.

Computed expressions, aliases and JSON extraction do not remove classification: `LOWER(email)`, `SUBSTRING(email,...)`, `HASH(email)`, JSON object wrapping or a fallback expression get a derivative classification and a permission check. The minimal variant rejects computations over a raw-sensitive source without `field.use`.

### 30.4. Usage controls

| Query without the matching raw-use grant | Expected behavior |
|---|---|
| SELECT * | Allowed fields; sensitive paths omitted |
| SELECT email | Masked output or generic denial, per policy |
| WHERE email = @value | Reject before index lookup |
| ORDER BY phone | Reject before sort/index access |
| GROUP BY email / COUNT(email) | Reject; field usage can reveal values/presence |
| WHERE email IS NULL / IS MISSING | Reject if usage on this path is closed |
| Text match/highlight on a sensitive field | Reject; indexing is disabled by default |
| VECTOR search in a derived-sensitive space | Reject without permission for this vector space |
| Export / CDC / live query | The same secure projector and a new auth checkpoint |
| Explain/Profile/error | No raw values; guarded statistics and paths |

Partial masking, for example the last 4 characters, is an allowed disclosure policy. The minimal implementation may support only omit, to reduce side channels. Built-in masking does not claim differential privacy or full indistinguishability of timing/access patterns.

### 30.5. Write semantics

A principal may have permission to write a sensitive field without permission to read it, for example an ingestion service. The write API checks allowed paths, the expected revision and classification changes. A masked response must not accidentally be used as a destructive full replacement.

The SDK therefore retains redaction metadata. A full PUT from a redacted source object requires an explicit replacement intent and permissions for every sensitive field it may erase. The safe ordinary path is a PATCH of allowed paths with CAS. `Remove` of a sensitive field is checked as a sensitive mutation; empty, missing and null have different semantics.

A client-provided sensitivity hint may raise classification only under a defined policy. Lowering classification, enabling `embeddingSource` or moving sensitive data into an unrestricted field requires a privileged workflow. Free text may contain unmarked PII; responsibility for classification/ingestion validation is described separately.

### 30.6. Indexes, embeddings and backups

A schema classification change invalidates query plans and the affected projection generations. Sensitive text that has already entered postings/stored snippets is removed, or gets a separate authorized index, before serving. Derived embeddings carry source-field lineage; an unsafe space stops serving until a rebuild or a permission correction.

Raw data may remain in the WAL, older revisions, snapshots and backups until retention/purge. Administrative logical deletion does not promise immediate physical erasure of every copy. Purge jobs have a scope, retention status and audit. Field masking is not applied as a transformation of data copies; copies travel only through Orleans calls, and the raw backup destination has a privileged authenticated boundary.

### 30.7. Minimal privacy test suite

Tests cover aliases, nested arrays, quoted paths, projection wildcards, functions, filters, sort/group/facets, generated snippets, vector fields, graph paths, cursor reuse, a revoked key, a changed role and exports. A canary sensitive value must never appear in a response, trace, exception text, audit payload or an authorized-to-outsider inference request.

A separate adversarial inference test runs adaptive predicates over a closed field; the binder must reject them before access. Row-count queries see only the authorized row scope. Strong noninterference between overlapping ACLs in shared statistics/ANN requires a separate security-domain design and is not covered by this minimal suite.

## 31. Where KeyLoad speed can come from

### 31.1. Performance hypothesis

The core hypothesis: an entity/affinity-aware layout, a short prepared query path, batched adjacency and data-local hybrid execution can reduce latency and resource cost for project-scoped mixed workloads. Until benchmarked, this is an engineering hypothesis.

No advantage is established for general relational OLTP or arbitrary joins. The first workloads to test are project-scoped search, graph-neighborhood retrieval, document CAS with several indexes and batch event ingestion.

### 31.2. Optimizations in order of verification

First remove unnecessary fan-out and redundant reads. Then reduce materialization/copy cost. Then tune local parallelism, SIMD and batching. At every step the benchmark preserves correctness, durability and the permission profile.

Late materialization passes IDs, revisions and scores between operators. JSON is read only for final hits or for an allowed residual predicate. Index covering fields are used only when their classification allows that use.

A `RecordBatch` executor may hold the required typed columns for a few hundred rows. This is an execution layout; the canonical payload stays JSON. Batch sizes 128/256/512 are benchmark candidates. Large buffers have lifetime/lease contracts; execution/managed RSS and allocation pressure are measured together.

The plan cache key contains the normalized AST, schema/index versions, field-policy shape and ranking profile. Principal-specific allowlists are stored separately with the policy epoch. The plan cache holds no literal PII and never carries grants between principals.

### 31.3. Speed limits

A full float32 scan of 1M vectors × 1536 dimensions reads at least 6.144 GB of raw vector bytes. At an **assumed effective bandwidth of 40 GB/s**, the ideal time for the read alone is `6.144/40 = 0.1536 s`. This calculation rests on an assumption that must be measured on the actual machine; distance computation, heaps, concurrency and storage add cost.

If scalar/ACL/graph filters leave 1000 vectors, the raw bytes to score are about 6.144 MB. This difference justifies adaptive exact-vs-ANN planning. The cost of building the allowlist and of random memory access counts toward end-to-end query latency.

Fan-out affects tail latency. Assuming independence and a 99% probability that each of 32 shards answers within the deadline, the probability that all answer in time is `0.99^32 ≈ 72.50%`. Real latencies may correlate; the example shows the need for pruning, budgets and slow-shard metrics.

For 32 shards × 200 candidates × 3 branches × an assumed 80 bytes per candidate, the network payload is about 1.536 MB before protocol overhead. Sending full JSON and vectors to the coordinator would increase this substantially. The actual candidate record size is checked by a serialization benchmark.

### 31.4. Memory and disk budget

Node budget = canonical memtables/cache + graph/index structures + query working sets + compaction/rebuild reserves + runtime + WAL/journals. Individual caches must not each treat all RAM as their own limit.

The copy count multiplies canonical storage; durable logs, projection snapshots and compaction have their own write amplification. Rebuilding two index generations at once is counted in admission. Low free disk moves the node into controlled throttling, read-only or fail-stop before any false durable ACK.

A public QPS figure without document size, index count, durability, hardware, concurrency, recall and filter distribution is not used as performance evidence. Recording these dimensions is part of the benchmark artifact.

## 32. Reproducible comparison with PostgreSQL

### 32.1. Baselines

**PG-A:** PostgreSQL 18 with a pinned patch, `jsonb` documents, tuned B-tree/expression/GIN indexes, edge adjacency tables and time partitioning where needed. Full-text uses the documented tsvector/tsquery ranking. The exact DDL/plans are part of the benchmark repository. [S36][S37]

**PG-B:** the same PostgreSQL with pinned pgvector, exact vector search, HNSW/IVFFlat, iterative scans, explicit `ef_search`/probes, filter indexes and partition strategies. Recall is compared under the same allowed-ID predicate. [S38]

**PG-C:** PostgreSQL with a BM25 extension, for example ParadeDB pg_search, plus pgvector for hybrid search. ParadeDB's official material describes BM25 + pgvector + RRF. Each extension version is checked separately for PostgreSQL support, license, replication and deployment options. [S41]

PG-A full-text ranking and BM25 are different relevance functions. A pure latency comparison without relevance evaluation would be incomplete. PG-C adds a closer lexical ranking baseline; its profile/weights/candidate windows are controlled by the benchmark.

### 32.2. Fairness rules

Same datasets, embeddings, query distribution, result projection, TLS/network path, acceptable freshness and durability contract. PostgreSQL benchmarks use prepared commands and a connection pool. Indexes are created and statistics updated before the warm benchmark; ingestion/update-under-load tests run separately.

Two levels are compared: the direct database API and an identical minimal application endpoint over each database. Embedded KeyLoad results are labelled in-process and are not mixed with remote PostgreSQL results.

For single-node durability, PG uses `fsync=on`, `synchronous_commit=on` and ordinary persistence guarantees. For a comparison with three copies, PG synchronous replication is configured to wait for the required number of standbys; topology, synchronous_standby_names and failure assumptions are recorded. `synchronous_commit=on` in a documented synchronous setup waits for a durable standby flush. Exact guarantee equivalence is checked by fault tests. [S39]

For the authorization baseline, PG uses a nonowner/non-BYPASSRLS role and matching policies. The comparison with KeyLoad includes masking/projection overhead when that feature is enabled. A separate no-policy benchmark is allowed as a correctly labelled microbenchmark. [S35]

PG JSONB GIN indexes are created for the relevant operators; expression indexes for hot typed fields, composite indexes for filter+sort. The graph baseline uses adjacency B-tree indexes and batched SQL/recursive CTE with the same depth/visited budgets. The vector benchmark under a selective filter uses an iterative scan or exact fallback when that is better. [S37][S38]

### 32.3. Workload matrix

| ID | Scenario | Primary control |
|---|---|---|
| B01 | Prepared Get/MultiGet, payload 1/4/16 KiB | p99, throughput, bytes/op |
| B02 | Put/Patch/CAS, 0/3/10 strict indexes | Durable ACK, conflicts, write amplification |
| B03 | Scalar filter + composite sort + cursor | Scanned records, stable order, allocations |
| B04 | Graph 1/2/3/5 hops, cycles, supernodes | Reference traversal result, RPC rounds |
| B05 | Exact/ANN search, filters 100/10/1/0.1% | Recall@10/@100, latency, memory |
| B06 | Filter correlation: independent/positive/negative | Recall collapse, exact fallback cost |
| B07 | BM25 + vector + graph scope + same safe projection | nDCG@10, candidate recall, p99 |
| B08 | Seeded graph retrieval + three-way fusion | Exact profile fixtures, end-to-end relevance |
| B09 | Series batch append/range/rollups/late events | Bytes/sample, aggregates, ingestion lag |
| B10 | Concurrent updates + reindex + compaction | Tail latency, freshness, bounded RSS |
| B11 | Tenant skew: small/large tenants/hot key | Fairness, overload outcomes, routing cost |
| B12 | ACL revoke/masking/CDC/cursor continuation | Zero forbidden output within the declared boundary |
| B13 | Node loss, network partition, retry storm | Durable outcomes, recovery, error rate |

Dataset sizes: small 100k, main 1M, large 10M or enough data for a working set larger than RAM. The corpus includes Ukrainian/English text, identifiers, short/long documents and heterogeneous schemas. Embeddings are identical across systems; model inference time is a separate metric.

### 32.4. Load methodology

Microtests measure allocation and operator CPU. End-to-end tests use both closed-loop clients and open-loop offered load to expose queue buildup and overload. Raw results keep scheduled start, actual start, completion, deadline/error and bytes. Timeouts and rejects are not removed from the report.

Warm-up, JIT, cache warming, checkpoint/compaction states and restart recovery are documented. Steady-state comparisons repeat runs with the same seed and with several different seeds. Confidence intervals or a variability report accompany medians; a single best run is not a result.

Scale comparisons use the approved one-node and three-node topologies with equivalent total resources and explicit persisted-copy acknowledgements. PostgreSQL partitioning on one server is not called distributed sharding. A distributed PostgreSQL arm requires an explicitly selected and qualified sharding stack; without one, single-node PG remains a single-node baseline.

### 32.5. "Worth continuing" criterion

One primary workload and one primary target are approved before the test. Example target: **at least 2× useful throughput at the same p99 SLO**, hardware budget, quality floor, projection freshness and durability. An alternative target may be a 50% latency reduction at the same offered load; switching the criterion after seeing results without marking the run exploratory is forbidden.

All outcomes have the status "not measured". Meeting the target in one workload does not extend to all SQL, graph and vector queries. If the gate fails, an operator-level analysis follows: read-view serialization, fan-out, allocations, index layout, disk or ANN recall. Any decision to change the architecture rests on that data.

The product is also assessed on deployment complexity, number of components, adoption cost and .NET developer experience. These complement performance evidence; the benchmark requirement against PostgreSQL still applies.

## 33. Useful features beyond the core query path

The product should include Explain/Profile, command outcome lookup, resumable CDC, provenance/source references, index status and readable diagnostics. They are needed to understand errors and to operate the system.

Resumable CDC/live queries use a dedicated change-feed profile; durable business subscriptions and queue delivery are described in Section 37 to Section 44. Live queries are built as query subscriptions on the committed change stream with their own quotas. SQL WHERE, permissions and masking are the same as for an ordinary query. Subscriptions may be limited to simple scalar predicates. Subscribing to an arbitrary expensive graph/ANN query that re-executes on every change requires a separate cost contract.

Document revision history has configurable retention, a storage quota and audit metadata. TTL events, tombstones and the outbox provide the basis for deletion and reprocessing; the history of sensitive values stays under the same raw-read grants.

Named vector spaces, multivector/chunk grouping, sparse retrieval, an optional reranker and reusable ranking profiles depend on stable candidate semantics. Query feedback is stored under a privacy policy; learning-to-rank requires a separate train/validation/held-out split.

The index advisor recommends indexes from redacted query shapes and explain statistics. Automatic index creation has hard quotas and an approval policy. Arbitrary loading of user assemblies, SQL UDFs, inference models or opening filesystem paths in the server process is out of scope.

## 34. How the implementation is assembled

### 34.1. Solution boundaries

The public SDK depends only on versioned contracts in `KeyLoad.Abstractions`; the SDK contains no storage, parser or search internals. `KeyLoad.Query` has Syntax/Binding/Planning/Execution; authorization evaluation lives in `KeyLoad.Core`.

```text
KeyLoad.Client ──────────────────────── KeyLoad.Abstractions
                                             ▲
KeyLoad ── Query ── Core ───────────────┤
                    │       │                 │
            Search adapters  Storage.ZoneTree
                    │       │
   Orleans grains (Clustering) ── ZoneTree owner Grain Service
                                   │
                  Replication: copies via Orleans calls
```

The arrows show logical relations; the compile-time dependency graph is enforced by architecture tests. The server composition root wires implementations through DI.

### 34.2. Startup

Each node: read configuration (`KeyLoad:SiloCount`, `KeyLoad:ReplicaCount`, data directory) → start the Orleans silo with the clustering provider → in the silo lifecycle, the ZoneTree owner Grain Service takes the exclusive data lock, checks the format and recovers canonical storage → load catalog/security → recover index generations → ready endpoints. Readiness is reported separately for canonical CRUD and for each search capability; public listening and accepted data traffic have different readiness checks. Orleans provides membership, discovery and the directory; there is no separate metadata transport.

silo lifecycle stages and `IHostedService` components have a defined start/stop order. Shutdown stops admission, drains bounded jobs, completes writers/readers, persists checkpoints, and closes the maintainer and files. A shutdown timeout returns an explicit state; recovery on the next start does not rely on a clean shutdown.

## 36. ADRs and open engineering decisions

| ADR | Proposed decision | What the implementation must close |
|---|---|---|
| ADR-012 | T-SQL subset parsed by Microsoft.SqlServer.TransactSql.ScriptDom + versioned KeyLoad extensions | Grammar and semantic fixtures |
| ADR-013 | SQL/JSON/C# → one authorized AST | Equivalence and unsupported-expression tests |
| ADR-014 | RBAC + bounded row/field-use policies | Cross-interface enforcement |
| ADR-015 | Sensitive fields omitted by default + derived lineage | Masks, mutation safety, index/reranker privacy |
| ADR-018 | Global per-modality windows → weightedRrfV1 | Statistics scope, ties, candidate completeness |
| ADR-019 | Managed-first ANN; exact oracle mandatory | Qualified library/code, dependency audit |
| ADR-020 | One connection grain per client, independent query contexts | No per-client head-of-line blocking, bounded CPU |
| ADR-021 | PostgreSQL as the primary performance baseline | Comparable quality/durability/security evidence |
| ADR-022 | Current policy epoch at the request/page boundary | Revocation ACK, fail-closed cache proof |

### 36.1. Questions that remain qualification gates

ZoneTree transactional range view and durable flush contract; durable apply on every configured copy through Orleans calls under fault injection; managed HNSW persistence/concurrency; BM25 and tokenizer quality of the text provider; cost of security-domain statistics; the optimal number of ZoneTree instances per node; the configured failure model for distributed graph scope.

Answering these questions requires targeted prototypes and actual measurements.

## 37. All-in-one contract: data, events and delivery

### 37.1. Product model

KeyLoad combines document/KV storage, scalar indexes, a property graph, time series, vector/text/hybrid search, an Event Store and durable messaging. Shared components: the catalog, transaction domains, authorization, ordered commit, replication, recovery, the SDK and the resource governor. Capabilities are enabled through a capability manifest; a queue-only configuration can run without text/ANN indexes.

The main cross-cutting contract: **change a document, append a business event and enqueue a message in a co-located queue lane in one atomic commit**. All operations must resolve to the same AtomicPartitionId through the catalog binding in Section 41. For a destination in another atomic partition, the source commit stores a transfer intent, and delivery has its own status.

ZoneTree documents building event streams from ordered key ranges and queue-like storage from keys, claim state and transactions. KeyLoad implements the append, consumer ownership and delivery protocols. These building patterns show the primitives are suitable; the actual durability/read-view gates remain mandatory. [S46][S47]

### 37.2. Purpose of logs and models

| Level | Authoritative data and lifetime | User access |
|---|---|---|
| ZoneTree WAL / transaction log | Recovery of local committed mutations; trimming per the storage protocol | Internal diagnostic boundary |
| Command copies | The same ordered commands in the ZoneTree owners of `ReplicaCount` silos; catch-up through Orleans calls | Internal replication boundary |
| Domain Event Store | Versioned business facts; retention per the stream set rules | Append, ReadStream, Subscribe, Replay |
| CDC / projection outbox | Changes for projections and transfer intents; consumer/rebuild retention pins | Separate protected change-feed API |
| Work queue | Payload plus scheduled/ready/leased/terminal state | Enqueue, Claim, Ack, Nack, Renew |
| Topic / persistent subscription | Retained log plus independent delivery/checkpoint state per group | Publish, Subscribe, Seek/Replay per permissions |

The Event Store has its own public identities, retention and schema versions. Trimming WAL/journals or the projection outbox preserves business history according to its own contract. Exposing the internal WAL through the public event API is forbidden: its records have different semantics, payload scope and lifecycle.

### 37.3. Source of truth for an entity

For an ordinary collection, canonical state is the document; CDC describes changes. The application appends a domain event explicitly when it has business meaning. DocumentUpdated records alone do not give a complete OrderAccepted or PaymentAuthorized model.

For an event-sourced collection, the authoritative state is the stream. The current-state document is a projection with SourceStreamRevision and ReducerVersion. Generic PUT/PATCH of such a read model is closed to ordinary roles. Rebuild replays a deterministic reducer over the available history, and external side effects have a separate delivery pipeline. DocumentRevision for CAS and SourceStreamRevision are stored separately.

The authority mode is set in the catalog; it cannot be switched between CRUD and event-sourced over existing data by a configuration toggle.

## 38. Event Store: streams, revisions, replay and snapshots

### 38.1. Event envelope

The proposed StreamRef includes tenant/database, TransactionDomainId, AtomicPartitionId, StreamSetId, StreamId and StreamGeneration. Generation prevents stale references from being used after an explicit delete/recreate. For an ordinary append, the stream version starts at 1.

```text
EventId                 client-generated stable ID
StreamRef               full scoped identity
StreamRevision          monotonic number within the stream
EventType               for example OrderAccepted
SchemaVersion           payload contract version
OccurredAtUtc           business time supplied by the producer
RecordedAtUtc           acceptance time assigned by the server
CausationId             ID of the causing command/event
CorrelationId           workflow link
Payload + Headers       validated immutable bytes
ClassificationVersion   schema/field lineage for access policy
```

The producer clock does not determine append order. EventId and CorrelationId do not define order across partitions either. Headers have size limits, a whitelist of transport metadata and classification; secrets and raw PII should not be used in stream names or routing keys.

### 38.2. Append and optimistic concurrency

The API accepts ExpectedRevision.Exact(n), ExpectedRevision.NoStream or an explicit ExpectedRevision.Any. Exact compares the current stream head inside transaction apply. NoStream requires that the stream generation does not exist. Any allows an append without a business concurrency check and must be a deliberate client choice.

One transaction appends the whole event batch, updates the head, writes event-ID dedup entries, feed entries and the command outcome. A successful append yields a contiguous range of revisions. A precondition conflict rejects the whole batch. TailRevision is kept after old events are removed by retention; the next append never reuses their numbers.

Idempotency is checked before any repeated OCC decision: the same CommandId and payload fingerprint returns the stored outcome. A repeated EventId with a different payload yields Conflict. The EventId scope is defined by StreamRef; a multi-event batch fingerprint includes order, headers and expected revision. Retention of command/event dedup entries fixes the guaranteed retry horizon. An arbitrary retry after the dedup metadata is removed gets no promise of indefinite deduplication. A mixed batch with some existing and some new EventIds is rejected as DuplicateEventId; a retry of a complete earlier batch resolves to its persisted command outcome. This removes the ambiguity of a partial append during retry.

KurrentDB is the semantic reference: its .NET API has expected state/revision and atomic append; its current documentation also describes multi-stream operations. The scope of these operations in the competitor benchmark is recorded separately from KeyLoad's atomic-partition contract. [S48]

### 38.3. ReadStream, catch-up and live tail

ReadStream takes fromRevision, direction, maxEvents, maxBytes and a stable committed cut for the page. Stream metadata returns FirstAvailableRevision, TailRevision and retention status. A request for already removed history returns HistoryUnavailable/CursorExpired with permitted diagnostics.

A catch-up subscription reads retained records, then continues the tail from a durable cursor. To close the race between the last scan and wake-up registration, the consumer registers for change notifications and then rechecks cursor/tail. A new-data notification is a hint; reconnects and lost wake-ups recover by re-reading committed state.

Global feed v1 has a stable EventSequence within an AtomicPartitionId. EventSequence is written together with the events and the feed index; it differs from StreamRevision and CommandSequence. A feed cursor across several partitions contains a vector of positions, a partition-discovery epoch and ClusterIncarnation.

For a broad subscription across many entity-scoped atomic partitions, the cursor is passed as an opaque server-side handle to a persisted position map. The wire carries a bounded token; active map pages and the number of source partitions have quotas. A query within a single stream uses a compact stream cursor. The server-side map does not remove the storage cost of checkpointing: E06 measures cardinality groups × source partitions.

The contract gives order within a stream and within each atomic feed. A cluster-wide total order would require additional serialization and a separate throughput/availability contract. Merging by RecordedAtUtc can serve as a display order with a tie-break; it must not be used as proof of global causal/commit order.

### 38.4. Snapshots, schemas and projections

An aggregate snapshot contains StreamRef, SourceRevision, ReducerVersion, StateSchemaVersion, a checksum and the state. Rebuild selects a compatible snapshot and the following events. A snapshot does not restore arbitrary historical queries over already erased payloads. Retention must state separately which replay options remain supported.

Current-schema replay accepts only the exact EventData.SchemaVersion given by the caller's reducer and a compatible snapshot ReducerVersion/StateSchemaVersion. An incompatible schema is rejected before the callback; there is no schema conversion, upcaster, resolver, automatic compatibility or fallback. Original event, snapshot and receipt bytes stay unchanged until explicit retention/purge. The reducer version defines the exact current contract and passes deterministic fixtures. Application reducers run as a bounded pure worker outside the storage host; cancellation, complete tail and JSON/state/input limits are checked before and between callbacks. Arbitrary user assemblies are never loaded into the storage host.

A snapshot/projection write checks the expected previous SourceRevision. For asynchronous models, the read-model change, inbox receipt and matching checkpoint must be coordinated atomically in the target scope or through an idempotent transfer. Events arriving during rebuild are handled by a generation-aware pipeline. Delivery of external messages from replay is off by default; explicit redrive has its own execution ID and audit.

## 39. Durable queues: claims, leases, ACK and retry

### 39.1. Public queue contract

QueueRef names a logical queue. QueueLaneRef adds the AtomicPartitionId in which its local part physically lives. A consumer reads one lane or a bounded set of lanes chosen by server-side routing. A work queue has a single execution state per message; several workers compete for available messages.

```text
Scheduled ── PromoteDue ──> Ready ── Claim ──> Leased ── Ack ──> Acked
                              ▲                 │
                              │                 ├── Nack / ExpireLease
                              │                 │         │
                              └──── retry ──────┘         ↓
                                                     Scheduled

MaxAttempts ──> DeadLettered
Explicit policy ──> Cancelled / Expired
```

At-least-once delivery means a message may be redelivered after an unknown outcome, consumer failure or lease expiry. Retries apply within retention, TTL, the retry/MaxAttempts policy and cluster availability. Terminal states and parked messages are visible to the operator. Successful execution of an arbitrary handler is not a storage-layer guarantee.

Publisher commit receipt, delivery ACK receipt and application effect receipt are distinct. RabbitMQ documents separate publisher confirms and consumer acknowledgements; this is a useful basis for checking our API boundaries. [S50]

### 39.2. Minimal storage layout

All keys have a tenant/AtomicPartition prefix and a versioned binary encoding. The graph namespace E keeps its meaning; the Event Store uses EV to avoid a collision.

| Namespace | Scoped key | Value |
|---|---|---|
| SH | streamSet / stream / generation | TailRevision, retention/schema metadata |
| EV | streamSet / stream / generation / revision | Immutable event envelope |
| ED | streamRef / eventId | Fingerprint and committed revision |
| EF | eventSequence / ordinal | EventRef, commit metadata |
| ES | streamRef / sourceRevision / reducerVersion | Aggregate snapshot |
| QB | queue / messageId | Immutable body and classified headers |
| QM | queue / messageId | State, attempts, readySequence, NotBefore, expiry, leaseVersion |
| QR | queue / readySequence / messageId | Ready reference |
| QS | queue / notBeforeUtc / messageId | Scheduled reference |
| QL | queue / leaseUntilUtc / messageId / leaseVersion | Expiry candidate |
| QD | queue / parkedSequence / messageId | Dead-letter reference, sanitized reason |
| SG | subscription / generation | Filter/configuration, source set, group metadata |
| SC | group / sourcePartition | Contiguous checkpoint, bounded gap state |
| SD | group / sourcePartition / eventSequence | Active delivery state |
| IN | handlerScope / executionGeneration / inputId | Inbox receipt and effect outcome |

QR/QS/QL entries change atomically with QM. The queue body is separate from frequently changing state, so ACK/renew does not rewrite a large payload. Secondary summaries used to find active lanes can be rebuilt; authoritative eligibility is checked against QM/QR during ordered apply. A large number of lanes requires local QXReady(queue, lane) and QXDue(deadline, lane, message, stateVersion) indexes. They are updated in the same local commit as the canonical lane state. Scheduler/receive use them for a bounded seek instead of a full scan of all AtomicPartitionIds. Recovery rebuilds and checks these summaries before readiness; stale entries are rechecked at apply. The fair-scan budget over ready lanes and the absence of skipped lanes are verified separately.

ReadySequence defines the current readiness order. After a delayed retry, the message gets a new ready position according to the queue profile. The default profile does not promise global FIFO across lanes. Priority buckets are a separate profile with anti-starvation tests.

### 39.3. ClaimBatch and delivery

The consumer sends ReceiveRequestId, maxMessages/maxBytes and the lease duration. The server checks permissions, inflight quota and resource budget, finds bounded candidates and submits a Claim command. Apply rechecks that each candidate is still Ready and eligible for delivery. Claim records the current owner, leaseVersion/nonce, attempt and deadline.

Payload and DeliveryToken are returned after the commit with the declared durability. A multi-lane receive consists of separate lane outcomes; the API reports partial claims honestly. Long polling waits for readiness without holding a transaction, the apply gate or a long grain turn.

The same ReceiveRequestId within the dedup horizon returns the result of the same claims; if the leases are no longer valid, the API returns an explicit expired outcome. A lost response or client cancellation can leave a committed lease until it expires. A retry without a stable request ID can create additional inflight claims, which are bounded by quota.

### 39.4. ACK, NACK, renew and lease fencing

DeliveryToken contains the tenant/queue/lane/message scope, the principal or authorized consumer identity, leaseVersion, the group generation where needed and ClusterIncarnation. It is integrity-protected. Caller identity is checked separately; the token does not replace the current permission check.

Ack/Nack/ExtendLease apply only to the matching current lease version. An old worker cannot acknowledge or change the new worker's lease after redelivery. An idempotent retry of an already committed AckCommand returns the earlier outcome within the retry horizon, with no new effect. Detailed timing rules are in Section 42.3.

One current persisted lease gives exactly one valid holder the right to perform the protected DB commit. A worker with an expired token may physically keep running. Preventing its repeated external actions requires application idempotency/reconciliation; KeyLoad's lease-version fencing covers only its own commit path.

### 39.5. Retry, DLQ, ordering and backpressure

The retry policy sets maxAttempts, base/max delay, the exponential factor and jitter. The chosen retry time is stored in the command so every copy reproduces the same decision. The standard profile forbids an immediate infinite requeue loop. Delivery attempts and business failure reasons are counted separately: a lost claim response can also consume a delivery attempt.

A DLQ in the same lane moves the message into a terminal state and stores the parked reference atomically. A remote DLQ uses the transfer intent from Section 41.4. If the destination is unavailable or full, the message stays PendingDeadLetter with a protected payload; the lossless profile never drops a payload to clear a backlog. Redrive requires an explicit admin grant, a destination policy check and a new delivery generation.

An OrderingKey routes to one lane. The StrictPerKey profile allows one active message per key; a retry of the earlier message blocks the next one. A stopped or parked head message has an explicit Continue/Block policy. The default competing-consumer profile gives ready-order dispatch within a lane; the actual completion order depends on workers and retries. The server makes no ordering claim about arbitrary external side effects.

For millions of messages/buckets, large immutable event/queue payloads could move into sealed segments; that option requires versioned blob references, checksummed manifests, atomic publication, integration with copying through Orleans and crash-safe GC. The current design uses the shared transactional engine with separate body/state keys.

Queue quotas bound stored bytes/messages, inflight bytes/count, receive waiters, attempts and DLQ storage. Control traffic, ACK and renew get a resource reserve. RabbitMQ quorum queues are a specialized benchmark for replication/delivery and large-backlog behavior; KeyLoad's own semantics are defined independently. [S51]

## 40. Topics, consumer groups and subscriptions

### 40.1. Work queue and pub/sub profiles

For "process the document with one worker", use a WorkQueue. For "every subsystem must process the event", use a retained Event Stream/Topic plus independent durable groups.

```text
DocumentUploaded
    ├── group: embeddings    → workers 1..N
    ├── group: audit         → workers 1..M
    └── group: notifications → workers 1..K
```

Each group has its own delivery states, retry policy and checkpoint. Workers within a group share the work. The event payload can be stored once while groups hold references and bounded active windows. Physically duplicating the payload per group is not required by the model; retention pins account for every required consumer.

KurrentDB persistent subscriptions have server-side checkpoints and competing consumers, including ACK/NACK. KeyLoad's own ordering profiles must be verified separately; binding a stream to a worker does not by itself guarantee order across retries. [S49]

### 40.2. Checkpoints and parallel processing

A group checkpoint advances along the contiguous acknowledged prefix. If 101 and 103 are complete and 102 is still in flight, the checkpoint does not jump past 102. A bounded gap bitmap/ranges records completed positions above the prefix. When gaps exhaust their budget, new delivery slows down or pauses.

A skip by the subscription filter is an explicit deterministic outcome bound to the filter generation. An authorization failure on required data stops the affected delivery/group according to policy; in full-replay mode the checkpoint never silently advances past an inaccessible event. Parking under the configured policy ends the primary attempt sequence and creates an audit/park record; replaying parked items has separate semantics.

Consumer-group membership/rebalance has a persisted generation/epoch. After an assignment change, old delivery tokens cannot acknowledge new assignments. Revocation is checked for the next delivery, the next page and each ACK command. The group's application service account has an explicitly defined data scope.

### 40.3. Start positions and partition discovery

CreateSubscription takes FromBeginning, FromNow or FromCursor. FromNow fixes a cut of the current source partitions together with the discovery epoch. Records that appear after the cut are available through catch-up; new partitions are registered in the coverage map from a defined start position. The resume cursor includes source coverage, so a topology change does not lose newly created lanes.

Changing the predicate or source set creates a new subscription generation. A group created later can replay only history still within retention. CursorExpired/HistoryUnavailable carries permitted information about the earliest position; automatically jumping to the tail requires an explicit opt-in.

A cross-partition subscription returns a vector of checkpoints. The consumer can configure per-stream/per-key ordering and its own causal logic. The architecture adds no global counter per event mutation.

### 40.4. CDC and internal projections

Resumable CDC uses the protected committed change feed. Event Store business events have their own public contract. Internal index workers read the system outbox under a reserved consumer identity; disabling public queues does not stop the mandatory maintenance of scalar/text/vector projections.

Each projection records its origin/reducer identity and source position. By default, source filters exclude a projection's own derived mutations, so a projection never loops on its own changes. Automatic routing of events → jobs is allowed only for versioned declarative rules with authorization, quotas and bounded fanout.

## 41. One transaction for documents, events and messages

### 41.1. TransactionDomain and catalog binding

The partition mapping of tenant + collection + partitionKey includes an explicit TransactionDomainId. A domain groups resources that may be co-located and take part in a shared batch. For resources outside a shared domain, the catalog can create an isolated domain that keeps per-resource partitioning.

```text
AtomicPartitionId = Resolve(
    TenantId,
    DatabaseId,
    TransactionDomainId,
    VersionedPartitionKey)
```

Collections, stream sets and queues reference their domain in the catalog. The same string `order-42` in two unrelated resource definitions does not grant a shared commit. The client passes a domain reference; the server checks each resource binding, the partition key type and the principal's scope.

Example: domain `order-processing` groups collection `orders`, stream set `order-events` and the lanes of queues `send-email`/`enrich-order`. PartitionKey `order-42` resolves to one AtomicPartitionId for all of these resources. The node's ZoneTree owner stores many such order partitions; no separate ZoneTree engine per order is needed.

### 41.2. Producer atomic batch

```text
Authorize all operations + resolve one AtomicPartitionId
                            ↓
                 Dedup CommandId / fingerprint
                            ↓
                     Ordered commit/apply
                            ↓
  Document + strict indexes + explicit domain events
  + stream heads + event feed + queue body/ready state
  + projection outbox + outcome + apply position
                            ↓
                 Required persistence barrier
                            ↓
                       CommitReceipt
```

Preconditions of all mutations are checked in one atomic scope. A failed stream revision, document CAS, permission or queue quota rejects the batch. Changes of one canonical batch become visible together under the verified read-view contract. Text/ANN projections have their own watermarks and may lag.

A queue lane belongs to the same atomic partition. Global consumer discovery aggregates lanes asynchronously, and periodic catch-up keeps them discoverable after a lost notification. Local queue acceptance in a CommitReceipt means committed message state. External handler completion is reported by a separate event/receipt.

### 41.3. Consumer atomic effects

CommitProcessing accepts a DeliveryToken, HandlerScope/ExecutionGeneration and a bounded set of database mutations. The transaction checks the current lease, input identity, inbox receipt and all preconditions; it then stores documents/events/outgoing messages, the inbox outcome and the ACK. All records must be in one AtomicPartitionId.

After a lost response, a retry with the same processing command returns the outcome. Redelivery of an already processed InputId does not reapply its database effects within the declared inbox horizon. The formal guarantee is **one committed effect per input within the defined transactional scope and dedup horizon**. The handler's CPU work may run more than once.

For an input from another partition, the target transaction atomically stores inbox + effects; the source ACK runs after that commit. A crash between these steps causes redelivery, which the target inbox stops. This sequence has its own status: eventual source acknowledgement. All output intents that must survive a handler crash are written together with the target effects.

External calls, such as email, payments or third-party HTTP APIs, run outside the database transaction. They use a stable provider idempotency key, a persisted intent/result and reconciliation. When the receiver does not support the required deduplication, the API/UI explicitly shows a possible duplicate/unknown outcome. A KeyLoad ACK does not prove that any external action succeeded.

### 41.4. Remote queues and transfers

For output to another atomic partition, the source transaction adds a TransferIntent with a deterministic TransferId, destination reference, schema/policy version and payload or pinned reference. The dispatcher is a set of Orleans `[StatelessWorker]` grains: they buffer pending intents and, on an Orleans timer, perform an idempotent destination enqueue with the same TransferId through the normal grain → ZoneTree owner path, without checking leadership. The target receipt is stored locally, after which the source marks the intent Delivered.

The source commit receipt shows OutputPending. A transfer may be retried after a timeout; the destination dedup horizon must cover the entire possible source retry/redrive window. For a stronger lifecycle up to explicit source release, a transfer tombstone can be kept; unbounded retention of such state requires a quota/GC contract.

A reference payload cannot be deleted before the transfer completes or is explicitly cancelled with an audit record. A destination permission/schema failure parks the transfer with a visible outcome. Resources sharing one physical engine do not widen the public atomic-partition scope. A cross-partition bulk transfer has per-destination outcomes.

## 42. Orleans, workers and persisted scheduling

### 42.1. Runtime roles

```text
API / C# SDK
      ↓
Event/Queue Commands + Query Frontends
      ↓
Authorization + Domain Resolver + Resource Governor
      ↓
Connection grain → partition grains (Orleans)
      ↓
ZoneTree owner Grain Service on ReplicaCount silos + ordered commit
      ↓
Durable cursor/lease state → external worker delivery
```

Orleans stream providers behave according to their backing queue; Microsoft explicitly documents that memory-stream messages are lost on silo restart. KeyLoad durable delivery is based on persisted state in ZoneTree and its copies through Orleans calls. [S52]

Orleans separately documents delivery guarantees for grain calls. KeyLoad stores persisted application command identity and dedup independently of transport retry behavior. [S53]

Background coordination of queues, subscription groups and the scheduler uses Orleans `[StatelessWorker]` pools with helper-created logical keys and automatically managed, non-addressable activations. They accept events, buffer them in memory and, on an Orleans timer whose interval is a typed option (for example, 5 seconds), write a batch through the normal grain → ZoneTree owner path. No leadership, leader hints or singleton coordinators are used. They do not hold an open database transaction while a handler runs; storage remains the source of truth for claims/checkpoints.

### 42.2. Consumer execution

User handlers run in a .NET Worker Service, an ASP.NET application or another SDK client. Trusted host modules can process system projections under separate capability grants. Arbitrary assemblies from a client request are never loaded into the server process.

A client's connection grain holds bounded subscription state; independent receive requests and handler executions have bounded concurrency. No durable actor is created per message. Workers scale independently of storage nodes; they connect to the public endpoint and receive lanes according to their current permissions.

The SDK helper runs receive → handler → renew when needed → CommitProcessing/Ack, with cancellation and unknown-outcome resolution. The helper must not automatically retry a non-idempotent external side effect. Handler idempotency and the external action contract are defined by the application.

### 42.3. Scheduler, lease time and failover

NotBeforeUtc, LeaseUntilUtc, ExpiresAtUtc and retries live in persisted records. A bounded in-memory heap or timing wheel holds the nearest deadlines; after a restart it is rebuilt from QS/QL range scans. The scheduler is a set of Orleans `[StatelessWorker]` grains managing many messages; the Orleans timer only triggers a check of durable due records and a batch write.

StatelessWorker grains write PromoteDue/ExpireLease/ExpireMessage commands in batches, with a recorded evaluation time (`TimeProvider`) and the expected state/lease version, through the normal grain → ZoneTree owner path; leadership is not checked. All copies apply the supplied values deterministically, and a stale command is rejected by its expected state/lease version. ACK/renew commands are also checked against the recorded evaluation time, the current lease version and the deadline. Copies never read the wall clock directly inside apply.

The persisted last-observed scheduling time bounds backward movement of the recorded evaluation time. There is no separate clock service or clock-sanity gate. Physical-time punctuality requires documented clock uncertainty and availability assumptions; millisecond-exact start times are not claimed.

An ExpireLease command carries the expected lease version and state revision/deadline. A concurrent renew that has already committed makes the old expiry command stale. Renew returns the new authoritative deadline; the consumer keeps a time margin. Cancellation or lease expiry does not stop another process's CPU work, so protected effects check fencing.

### 42.4. Resource isolation and modular deployment

Control plane, commit, ACK/renew, delivery, queries, reindex and compaction have separate concurrency/IO budgets. Prefetch has a strict memory budget. Backlog stays on disk; the active window is bounded. When backlog exceeds its quota, the producer receives ResourceExhausted before commit.

A single-node all-in-one deployment is one shared failure domain. Separating resources that share an atomic domain requires an explicit switch to the transfer protocol.

The feature manifest separates Documents, EventStore, WorkQueues, Topics, Scheduler, Graph, TimeSeries and Search. Internal recovery/outbox workers are part of the kernel. AMQP/Kafka/Redis wire compatibility and adapters for third-party .NET bus frameworks are separate capabilities with their own qualification.

## 43. SQL, SDK and end-to-end scenarios

### 43.1. Producer API

The following fragment is a **sketch of the KeyLoad API**. Types, method names and namespaces are not final. All resources in the example are bound in advance by the catalog to domain `order-processing`.

```csharp
// API sketch. The batch is built locally until a single CommitAsync.
var partition = client.ForPartition(
    transactionDomain: "order-processing",
    partitionKey: orderId);

var batch = partition.CreateBatch(commandId);

batch.Documents.Put(
    collection: "orders",
    id: orderId,
    value: updatedOrder,
    expectedRevision: documentRevision);

batch.Events.Append(
    streamSet: "order-events",
    streamId: $"order/{orderId}",
    events: new[] { orderAcceptedEvent },
    expectedRevision: ExpectedRevision.Exact(streamRevision));

batch.Queues.Enqueue(
    queue: "send-email",
    messageId: messageId,
    payload: new { OrderId = orderId, Template = "accepted" });

var receipt = await batch.CommitAsync(cancellationToken);
```

EventId, MessageId and CommandId are created once per logical operation and kept for retries. DocumentRevision and StreamRevision are independent. A network timeout returns UnknownWriteOutcome; checking the outcome or retrying uses the same CommandId. The builder does not open a server-side transaction while the object is being built.

Consumer API primitives: ReceiveBatch, Ack, Nack, ExtendLease, GetDeliveryOutcome, CommitProcessing. Management API: CreateQueue, PauseQueue, ResumeQueue, InspectMessages, ReadDeadLetters, Redrive, CreateSubscription, GetLag, SeekSubscription. Operations have scoped permissions and idempotent command IDs wherever they mutate state.

### 43.2. SQL for reading events and queue diagnostics

Proposed syntax, lowered into the shared authorized AST:

```sql
SELECT TOP (100) e.revision, e.eventType, e.recordedAt, e.payload
FROM EVENTS(@streamRef) AS e
WHERE e.revision > @afterRevision
ORDER BY e.revision;
```

```sql
SELECT TOP (50) m.messageId, m.state, m.attempts, m.notBefore
FROM QUEUE_MESSAGES(@queueRef) AS m
WHERE m.state = 'DeadLettered'
ORDER BY m.messageId;
```

EVENTS resolves the StreamRef and the stream-read policy. QUEUE_MESSAGES provides an authorized operational view; delivery tokens, secrets and the raw body are excluded without the corresponding grant. SELECT is read-only and does not change claims/checkpoints. Obtaining the right to execute a job requires an explicit Receive/Claim command.

Generic UPDATE/DELETE over internal stream, queue state or checkpoints is closed. Proposed APPEND/ENQUEUE SQL statements may compile into the same authorized commands; they do not create an alternative path around invariants. DDL/capability validation is the same for HTTP/SQL/SDK.

### 43.3. Event → job → document/vector → search

Knowledge pipeline example:

```text
Commit A, one domain partition:
  document status=uploaded
  + event DocumentUploaded
  + queue job GenerateEmbedding
                    ↓
Worker: model inference outside the database transaction
                    ↓
Commit B, the same domain partition:
  verify input revision + delivery token
  + canonical vector + processing status
  + event EmbeddingGenerated + inbox receipt + ACK
                    ↓
Committed outbox → ANN/text projection → search watermark
```

If the document changed during inference, Commit B rejects the stale input revision. The retry/cancellation policy decides whether a new job is needed. Output to another partition uses the idempotent effect protocol of Section 41.3. The model and the permitted source fields are fixed in the job metadata so that PII does not reach a provider without a grant.

EmbeddingGenerated means a committed canonical result. ANN visibility is determined by WaitForIndex(receipt.Token). A job ACK is not a substitute for the search freshness barrier. This scenario combines the database, Event Store, work queue and hybrid search into one verifiable workflow.

### 43.4. Further capabilities

Delayed jobs, retries, recurring schedules with an explicit timezone/misfire policy, saga state and timeout messages can be built on this kernel. A recurring occurrence has a stable OccurrenceId for dedup. A general durable workflow engine with replay of user code, activity history and version-safe orchestration is a separate capability; its guarantees have their own tests.

Event payloads can be indexed for text/vector search under an opt-in profile. The result identity is EventRef; graph/time-series projections keep provenance to it. The default configuration does not automatically index all queue payloads for full-text and vector search, because of storage/CPU/privacy budgets.

## 44. PII, retention, backup and recovery for eventing

### 44.1. Permissions and masking

New capabilities: events.append/read/replay, streams.manage, queue.publish/consume/inspect, queue.ack/renew, subscriptions.manage, deadletters.read/redrive and scheduler.manage. Each has a tenant/database/resource scope. A publish grant does not automatically grant reading the backlog; a diagnostics grant does not grant obtaining a delivery token.

Event and message schemas have a classification catalog, source lineage and the current output policy. Rules apply to payloads, headers, SQL views, subscriptions, DLQ, export, tracing and client error text. Classification data is stored with the schema generation, and current restrictive overrides apply at the next delivery/read boundary.

Human inspection uses a safe projector. For a reducer/worker that requires raw fields, the capability declares the required data scope; a missing permission yields PermissionDenied/ParkedByPolicy. Silently substituting redacted JSON for a required input can change the business result, so this fallback is never applied automatically.

Where possible, an event holds a CustomerRef and business facts, while mutable contact data lives in a protected document. Archives, backups and copies keep their own raw-data threat model. Omitting a field from a JSON response does not erase its bytes from history.

### 44.2. Retention and privacy purge

Event streams can use KeepAllUntilQuota or an explicit bounded retention. KeepAllUntilQuota rejects new appends when the budget is exhausted. Bounded retention declares the earliest replay revision and the snapshot/archive rules. Work queues have retention for terminal receipts and a backlog policy; topics have an independent lifetime for retained events.

A slow consumer may pin required history within the configured quota. When the limit is reached, the policy selects paused ingestion, an explicit consumer failure/required resync or an approved discard profile. Silently erasing unread events while reporting a successful checkpoint is forbidden. The retention coordinator accounts for active rebuilds, transfer references and durable subscriptions.

A privileged Erase/Purge workflow can remove specific classified payloads from events, queues, DLQ and derived data while keeping a minimal authorized tombstone/audit. Stream positions keep their identity; replay reports RedactedEvent/HistoryUnavailable according to the contract. A hash of the old payload can itself be sensitive; retaining it requires a separate policy.

Complete physical purge covers WAL, copies, compaction, snapshots, backups, external archives and encryption keys. This design does not establish automatic legal compliance; a deployment must document its actual deletion timelines and limits. Crypto-erasure requires a separate key-lifecycle design and verification of all copies.

### 44.3. What a backup contains

The authoritative manifest covers domain bindings, stream generations/heads, event records and dedup, stable event sequences, queue bodies/states/ready/scheduled/lease indexes, group configs/checkpoints/gaps, inbox receipts, transfer intents and their retention pins. Rebuildable indexes have a validation/rebuild recipe. Full stream history cannot be restored if its canonical records were not included in the backup.

For an offline backup, all of these records belong to the same closed storage cut. Cross-partition transfer reconciliation must check the source intent and destination dedup of each restored store. External side effects performed before the snapshot are not rolled back with the database.

### 44.4. Restart, failover and restore of an older cut

After a normal restart, the server restores committed queue state and checkpoints before any new claims. Unfinished leases go through deterministic deadline/ownership evaluation. After a node loss, available copies may serve already committed data; new claims and ACKs require every configured copy. Duplicates remain allowed behavior of the at-least-once profile.

Restoring an older backup starts with **delivery paused** and a new ClusterIncarnation. Old delivery tokens/cursors are invalidated. MessageIds/InputIds are kept for reconciliation, and restored leases are marked for controlled re-evaluation before resume. The user reviews the redelivery risk for messages successfully processed after the backup cut and explicitly resumes dispatch.

A paused restore prevents automatic resending of emails, API calls and other irreversible actions. The SDK/operations guide explains how to obtain provider outcomes by idempotency keys. Without external receipts, a full reconstruction of the actions actually performed may be impossible.

Shutdown first stops new claims while keeping a budget for ACK/renew/drain; it then closes writers/readers and files. An abrupt crash is always tested separately from graceful shutdown. A confirmed durable ACK is not redelivered within the same incarnation during normal operation; explicit redrive and restore of a past cut have their own rules.

## 45. Performance and competitive baselines

### 45.1. Performance hypothesis and the cost of guarantees

KeyLoad's hypothesis: a co-located document/event/queue batch reduces application round trips and duplicated coordination for mixed workflows. Ordered seeks, small delivery state, batching and shared authorization can lower resource cost. Any advantage still requires measurement.

A full queue lifecycle usually includes a persisted enqueue, claim and ACK, plus renew/retry transitions when needed. Group commit can combine the flush of several operations; it does not remove replication, indexing or log bytes. Producer-side accepted-message throughput alone is not used as completed-work throughput.

Measured: publish commit p99, claim p99, durable ACK p99, ready-to-start latency, scheduled lateness, unique completed jobs/s, stream append/read/replay throughput, consumer lag, gap memory, bytes/message, write amplification, dedup retention cost and impact on document/search p99. Handler CPU/external API time is measured separately and within the end-to-end workflow.

### 45.2. Baselines

The PostgreSQL baseline includes indexed event rows `(streamId, generation, revision)`, stream head/CAS, queue lease state, inbox and outbox in ordinary transactions. Competing consumers use a short transaction SELECT FOR UPDATE SKIP LOCKED → update claim → COMMIT. PostgreSQL explicitly documents SKIP LOCKED as a mechanism for queue-like contention. The handler runs after the claim commits, without a long row lock over the whole business workload. [S56]

**Marten + Wolverine** is the mandatory combined .NET baseline: Marten handles documents/events, and the Wolverine integration stores outgoing messages until dispatch after commit. Wolverine has a PostgreSQL durability integration and database-backed messaging. Because this integration already exists, any KeyLoad advantage on a single transactional workflow must be proven in practice. [S54][S55]

RabbitMQ quorum queues are added as the specialized messaging arm, and KurrentDB as the specialized append/replay/subscriptions arm. [S48][S49][S51] A specialized arm is compared only on the declared shared workload. PostgreSQL synchronous replication, KeyLoad with three copies and competing systems receive explicitly recorded durability/failure assumptions. An on-disk queue is compared only with an on-disk queue.

### 45.3. Workload matrix for Event Store and messaging

| ID | Scenario | What to verify |
|---|---|---|
| E01 | Append batch 1/32/128; many streams and one hot stream | p99, OCC conflicts, no duplicate event versions |
| E02 | Stream forward/backward read, catch-up → live, >RAM replay | Throughput, missing/duplicate events, bounded memory |
| E03 | Producer enqueue → receive → ACK; 128 B/1 KiB/16 KiB bodies | Unique completed throughput, durable receipts |
| E04 | Worker crash, lost receive/ACK response, slow expired worker | Redelivery, receipt fencing, inbox effects |
| E05 | Delayed jobs, clock skew, restart near a deadline, renew race | Lateness, no premature claim within the declared clock bound |
| E06 | 1/10/100 groups; slow group and out-of-order completion | Contiguous checkpoint, fanout IO, gap/cache budget |
| E07 | Document + event + local enqueue, producer and consumer batches | Atomic invariants, PG/Marten/Wolverine comparison |
| E08 | Cross-partition transfer, duplicate deliveries, remote DLQ full | No lost intent/payload, bounded retry, visible terminal status |
| E09 | 100k/1M/larger-than-RAM backlog under ANN/reindex/compaction | Database p99, control-plane/ACK liveness, IO fairness |
| E10 | Retention pin, privacy purge, pause/restore/resume | CursorExpired, redaction, no automatic external replay |
| E11 | Node loss, total reset fault model | Acknowledged durable outcomes and preserved domain scope |

The numbers are test configurations, not measured capacities. Open-loop load, timeout/reject rates, hardware/versions, datasets, comparable policy overhead and raw samples are recorded per the rules of Section 32. The primary success target is set before the runs; messaging gains do not automatically extend to SQL or ANN workloads.

## 46. ADR-023..ADR-031 for eventing

| ADR | Decision | Verification criterion |
|---|---|---|
| ADR-023 | Domain events, CDC, WAL and queue state have separate lifecycles/authority | Trimming one journal does not break the public history contract |
| ADR-024 | Shared TransactionDomain through catalog resource binding | An atomic batch resolves identically after restart and activation migration |
| ADR-025 | Stream revision + stable per-atomic event feed positions | Cursor vector and retained history are correct after restart and activation migration |
| ADR-026 | At-least-once delivery, fenced leases, inbox and protected DB effects | Lost ACK/claim response and expired-handler tests |
| ADR-027 | Durable groups with contiguous checkpoint and bounded gaps | Parallel completion does not skip pending input |
| ADR-028 | Persisted scheduling; logged time evaluation in StatelessWorker batches | Copies and restarts produce identical transition outcomes |
| ADR-029 | Event/message classification, required-input grants, safe diagnostics | PII/token canaries, denial before unsafe replay |
| ADR-030 | Retention pins and paused restore with a new incarnation | No automatic external redelivery after restoring an older cut |
| ADR-031 | Modular all-in-one deployment, reserved control resources | Mixed-load liveness and an honest capability/protocol manifest |

### Summary

KeyLoad provides one managed environment for current state, business history and reliable processing. A shared catalog domain lets documents, stream events and local queue state commit together. Distributed transitions, side effects, retention and search freshness have explicit boundaries. These invariants must be proven by tests; scalability and any advantage over PostgreSQL/Marten/Wolverine require separate measurements.

## Sources

The sources describe public capabilities; production qualification uses separate acceptance gates. Online documentation changes; package versions and source pins are fixed in the central manifests.

[S1] ZoneTree repository and README: https://github.com/ZoneTree/ZoneTree

[S2] ZoneTree transactions: https://github.com/ZoneTree/ZoneTree/blob/main/docs/usage/transactions.md

[S3] ZoneTree WAL modes: https://github.com/ZoneTree/ZoneTree/blob/main/docs/durability/wal-modes.md

[S4] ZoneTree production checklist: https://github.com/ZoneTree/ZoneTree/blob/main/docs/operations/production-checklist.md

[S5] ZoneTree iteration and range scans: https://github.com/ZoneTree/ZoneTree/blob/main/docs/usage/iteration-and-range-scans.md

[S6] Microsoft Learn, grain directory: https://learn.microsoft.com/en-us/dotnet/orleans/host/grain-directory

[S7] Microsoft Orleans project documentation, grain directories: https://dotnet.github.io/orleans/docs/host/grain-directory/

[S8] Microsoft Learn, Orleans transactions: https://learn.microsoft.com/en-us/dotnet/orleans/grains/transactions

[S9] ZoneTree.FullTextSearch: https://github.com/ZoneTree/ZoneTree.FullTextSearch

[S11] USearch repository: https://github.com/unum-cloud/usearch

[S12] USearch C# SDK: https://unum-cloud.github.io/USearch/csharp/

[S15] Elasticsearch reciprocal rank fusion: https://www.elastic.co/docs/reference/elasticsearch/rest-apis/reciprocal-rank-fusion

[S16] ZoneTree partitioning and replication: https://zonetree.dev/docs/building-systems/partitioning-and-replication/

[S17] ZoneTree transactional interface: https://github.com/ZoneTree/ZoneTree/blob/main/src/ZoneTree/ITransactionalZoneTree.cs

[S18] Orleans task scheduler: https://learn.microsoft.com/en-us/dotnet/orleans/implementation/scheduler

[S19] Orleans request scheduling: https://dotnet.github.io/orleans/docs/grains/request-scheduling/

[S20] Orleans stateless worker grains: https://learn.microsoft.com/en-us/dotnet/orleans/grains/stateless-worker-grains

[S22] Qdrant Query API, hybrid/multi-stage queries and RRF conventions: https://qdrant.tech/documentation/search/hybrid-queries/

[S23] Weaviate filtering, allowlists and ACORN: https://docs.weaviate.io/weaviate/concepts/filtering

[S24] Vespa phased ranking: https://docs.vespa.ai/en/ranking/phased-ranking.html

[S25] Qdrant multitenancy: https://qdrant.tech/documentation/manage-data/multitenancy/

[S27] RavenDB RQL: https://docs.ravendb.net/7.2/querying/rql/what-is-rql

[S28] RavenDB vector search: https://www.ravendb.net/features/ai-features/vector-search

[S29] RavenDB time-series API: https://docs.ravendb.net/7.2/document-extensions/timeseries/client-api/overview

[S30] SurrealDB query language reference: https://surrealdb.com/docs/reference/query-language

[S31] SurrealDB search/fusion functions: https://surrealdb.com/docs/reference/query-language/functions/database-functions/search

[S32] ArangoDB vector functions: https://docs.arango.ai/arangodb/stable/aql/functions/vector/

[S33] ArangoDB graph traversals and pruning: https://docs.arango.ai/arangodb/stable/aql/graph-queries/traversals/

[S34] Neo4j Cypher SEARCH clause: https://neo4j.com/docs/cypher-manual/25/clauses/search/

[S35] PostgreSQL 18 row security: https://www.postgresql.org/docs/18/ddl-rowsecurity.html

[S36] PostgreSQL 18 full-text ranking: https://www.postgresql.org/docs/18/textsearch-controls.html

[S37] PostgreSQL 18 index types: https://www.postgresql.org/docs/18/indexes-types.html

[S38] pgvector filtering, multitenancy and iterative scans: https://github.com/pgvector/pgvector

[S39] PostgreSQL 18 WAL and synchronous commit: https://www.postgresql.org/docs/18/runtime-config-wal.html

[S40] SQL Server dynamic data masking and inference limitations: https://learn.microsoft.com/en-us/sql/relational-databases/security/dynamic-data-masking?view=sql-server-ver17

[S41] ParadeDB: hybrid search in PostgreSQL, October 22, 2025: https://www.paradedb.com/blog/hybrid-search-in-postgresql-the-missing-manual

[S42] HNSW original paper, abstract: Malkov/Yashunin: https://arxiv.org/abs/1603.09320

[S43] ACORN paper, abstract: predicate-aware ANN: https://arxiv.org/abs/2403.04871

[S44] NaviX paper, abstract: graph-aware vector retrieval: https://arxiv.org/abs/2506.23397

[S45] Microsoft: .NET platform overview: https://dotnet.microsoft.com/en-us/learn/dotnet/what-is-dotnet

[S46] ZoneTree, Event Stores: https://github.com/ZoneTree/ZoneTree/blob/main/docs/building-systems/event-stores.md

[S47] ZoneTree, Queues: https://github.com/ZoneTree/ZoneTree/blob/main/docs/building-systems/queues.md

[S48] KurrentDB .NET client, Appending events: expected revision and atomic appends: https://docs.kurrent.io/clients/dotnet/v1.4/appending-events

[S49] KurrentDB persistent subscriptions, checkpoints and consumer groups: https://docs.kurrent.io/clients/node/v1.0/persistent-subscriptions

[S50] RabbitMQ, Consumer Acknowledgements and Publisher Confirms: https://www.rabbitmq.com/docs/confirms

[S51] RabbitMQ, Quorum Queues: https://www.rabbitmq.com/docs/quorum-queues

[S52] Microsoft Orleans, stream providers and memory stream durability: https://learn.microsoft.com/en-us/dotnet/orleans/streaming/stream-providers

[S53] Microsoft Orleans, messaging delivery guarantees: https://learn.microsoft.com/en-us/dotnet/orleans/implementation/messaging-delivery-guarantees

[S54] Marten tutorial, integration with Wolverine: https://martendb.io/tutorials/wolverine-integration

[S55] Wolverine, PostgreSQL durability and transport: https://wolverinefx.net/guide/durability/postgresql

[S56] PostgreSQL 18, SELECT and SKIP LOCKED for queue-like workloads: https://www.postgresql.org/docs/18/sql-select.html

### Source verification limits

The sources are official documentation and selected GitHub README files. For ACORN, HNSW and NaviX only the abstracts are used; the paper experiments are not reproduced and their implementations are not audited. Online documentation can change, and publication/version labels do not replace pinned package/source qualification. Release pins and license checks are fixed in the central manifests.

The sources do not establish performance, fault guarantees, the dependency matrix or interoperability; these require running the systems. SDK/SQL examples are proposed designs. A framework's word-level exactly-once claim does not extend to arbitrary external effects; KeyLoad's contract is defined in Section 41.3.
