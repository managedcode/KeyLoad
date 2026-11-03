# Competitor execution paths and KeyLoad redesign priorities

Reviewed 2026-11-01. This is a source-grounded engineering assessment, not a benchmark victory claim. Local diagnostic timings and captures are retained below the functional test binaries; they are not public benchmark figures. No competitor source was copied and no engine or package was adopted.

## Findings that change the implementation priority

The confirmed common-path waste is an auxiliary database read of the current principal before operations which load and authorize that principal again. The credential's user grain already retains verified credential data and observes invalidation. Ordinary HTTP and SQL operations can authenticate the subject and let their operation owner enforce fresh persisted policy. MCP discovery, administrator/session establishment and file adapters which consume authority metadata retain that metadata read.

Independent request execution activations remain necessary. The real synchronous-concurrency test holds one execution thread and requires another request on the same connection and physical owner to complete. Orleans interleaving cannot preempt synchronous work. Removing request grains failed this design review and is not part of the implementation.

The follow-up active allocation profile attributes repeated list growth and component-array copies to `KeySpace.Partition`, and repeated typed serializer construction to our payload reader. Partition scope and suffix now share one encoder buffer; the reader uses Orleans' registered singleton typed serializer with an independent pooled session per decode. Literal key bytes, malformed envelopes, concurrent round trips and retained native records remain correctness gates. This is consumer API reuse, not a dependency defect or a change to storage formats.

The next targets are actual access and materialization work: scalar index-range seeks, exact vector top-K and graph count/vertex projection. An active allocation profile also identified repeated catalog decoding inside our storage adapter for every candidate point read. One immutable native read operation can reuse its own resource-mode decision while validating every complete key. This introduces no cross-request metadata authority or engine cache.

## Versions relevant to the repository benchmark

| Engine | Repository-selected image/version | Source inspected |
| --- | --- | --- |
| Qdrant | v1.17.1, pinned manifest digest | v1.17.1 |
| PostgreSQL + pgvector | pgvector 0.8.6, PostgreSQL 18 image, pinned manifest digest | pgvector v0.8.6; PostgreSQL REL_18_STABLE, not an assertion of an exact image patch-source match |
| TimescaleDB | 2.30.2-pg18, pinned manifest digest | 2.30.2 |
| KurrentDB | 26.1.2, pinned manifest digest | v26.1.2 |
| MongoDB | 8.3.9, pinned manifest digest | r8.3.9 |
| Neo4j | 2026.09.0, pinned manifest digest | 2026.09.0 |
| SurrealDB | v3.2.4, pinned manifest digest | v3.2.4 graph-key representation |

Version declarations come from `build/KeyLoad.LoadTests.AppHost/Resources/`. Public benchmark measurements still require the canonical Linux workflow and its original artifacts. Local macOS Debug measurements cannot be ranked directly against those figures.

## PostgreSQL: authenticate the session, execute commands in that session

[postinit.c](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/backend/utils/init/postinit.c) authenticates during backend initialization and establishes the session user. [postgres.c](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/backend/tcop/postgres.c) then handles Parse, Bind and Execute messages inside that backend. [execMain.c](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/backend/executor/execMain.c) checks relation permissions using the current user; authentication at connection time does not remove operation authorization or row security.

[plancache.c](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/backend/utils/cache/plancache.c) retains prepared analysis/plans and invalidates them for relevant schema, role and row-policy changes. [catcache.c](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/backend/utils/cache/catcache.c) separates catalog-cache hits from misses and handles invalidation while entries are being created. [nodeIndexscan.c](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/backend/executor/nodeIndexscan.c) passes scan keys to the index access method, fetches tuples under the execution snapshot and preserves interruption/predicate checks.

KeyLoad implication: keep the verified subject on the server-owned connection and carry operation identity separately. Perform fresh policy checks at their actual execution owner. Avoid an auxiliary full database operation solely to reconstruct the current principal before every operation. Reuse metadata inside its existing consistent cut; do not replace fresh policy authority with an unbounded or uninvalidateable cache.

[PostgreSQL HOT](https://www.postgresql.org/docs/18/storage-hot.html) avoids new
index entries when indexed columns remain unchanged and the heap-page conditions
allow it. KeyLoad's scalar index entries contain the entity ID. Its update path
now retains those entries when the entity identity and indexed components remain
equal, including unique-index ownership. Field-use authorization still runs;
changed values, deletion and uniqueness conflicts retain their existing atomic
behavior. Whole-operation controls compare native mutation counts with an
unindexed update and retain conflict, healthy-follow-up and cold-open checks.
Released-image throughput qualification remains open.

## Qdrant: batch exact scoring, retain top-K, preprocess cosine

[plain_vector_index.rs](https://github.com/qdrant/qdrant/blob/v1.17.1/lib/segment/src/index/plain_vector_index.rs) routes exact scanning through a batch searcher over vector storage and deletion/filter state. [point_scorer.rs](https://github.com/qdrant/qdrant/blob/v1.17.1/lib/segment/src/index/hnsw_index/point_scorer.rs) reuses chunk/score buffers and feeds a fixed-length priority queue. [metric_query_scorer.rs](https://github.com/qdrant/qdrant/blob/v1.17.1/lib/segment/src/vector_storage/query_scorer/metric_query_scorer.rs) scores borrowed dense batches. [simple.rs](https://github.com/qdrant/qdrant/blob/v1.17.1/lib/segment/src/spaces/simple.rs) preprocesses cosine vectors and uses hardware-specific dot-product kernels.

[tonic/auth.rs](https://github.com/qdrant/qdrant/blob/v1.17.1/src/tonic/auth.rs) authenticates each gRPC request and attaches its resolved access object to request extensions. Therefore not every competitor authenticates only once per connection. Its configured-key path can be an in-memory comparison; JWT/stateful validation has different work. The lesson is to avoid unnecessary database/routing work, not to remove identity checks indiscriminately.

The original `VectorRanker.Rank` appended every eligible `SearchScore` to a list and sorted that list. For N candidates and a requested K=10, that retains O(N) scores and performs O(N log N) sorting. A bounded exact top-K heap can retain O(K) scores with O(N log K) selection. Deterministic score/id ordering must stay identical. Hybrid rank fusion needs its own correctness proof before truncating branch rankings.

KeyLoad's benchmark requests exact cosine results. Replacing this with approximate HNSW would change the question. A normalized derived representation also needs cut/epoch validity and exact numerical/tie validation; modifying original vector bytes or introducing another storage authority is not justified by this review.

Exact search now encodes the document and lineage key prefixes once per scan and appends each candidate ID using the canonical key encoder. In a local macOS Arm64 Debug A/B/B/A with 100,000 vectors, one client and 20 verified queries per arm, mean process CPU per query was 630 ms before and 471 ms after. Allocation remained about 160 MB per query. Both arms returned identical IDs, scores, revisions and JSON at the same read cut. The SDK and Orleans transport were outside this measurement. The local logs and DLL/PDB hashes are retained under the functional test binaries in `performance-investigation/orleans-request-20261103/document-point-ab-20261103/`.

## pgvector: data layout and arithmetic are separate from request overhead

[vector.c](https://github.com/pgvector/pgvector/blob/v0.8.6/src/vector.c) computes exact distances over contiguous float arrays, using compiler-vectorized loops and target-specific compilation where supported. KeyLoad already has SIMD-backed `PreparedSimilarity`; the presence of SIMD alone is not the missing proof. The next vector profile must separate storage reads, record decoding, candidate selection, full sorting and metric arithmetic before changing kernels.

## MongoDB: specialized point lookup and catalog-aware resource ownership

[express_plan.h](https://github.com/mongodb/mongo/blob/r8.3.9/src/mongo/db/exec/express/express_plan.h) has an `_id` path that finds a single index entry, seeks the exact record and passes it to the continuation. It retains a collection acquisition/catalog identity and checks that identity when restoring resources. The clustered `_id` path can seek the record directly. KeyLoad also already has a direct document key, so adding another index or generic query planner to its point read would add work. The relevant differences are its surrounding request path and repeated metadata decoding.

## Neo4j and SurrealDB: traverse adjacency and project only the requested result

[BFSPruningVarExpandCursor.java](https://github.com/neo4j/neo4j/blob/2026.09.0/community/cypher/runtime-util/src/main/java/org/neo4j/internal/kernel/api/helpers/BFSPruningVarExpandCursor.java) maintains primitive node sets and a frontier, reads relationship/node cursors and emits distinct endpoints without enumerating every path. The class demonstrates a relevant execution primitive; no Neo4j EXPLAIN/PROFILE was collected, so this review does not assert that the repository's Cypher query selected this exact operator.

The benchmark's `Neo4jGraphDriver.CountReachableAsync` requests `count(DISTINCT t)` from the server and receives one scalar. The original `KeyLoadGraphDriver.CountReachableAsync` called the full `TraverseAsync`, received the complete graph traversal and counted its vertices on the client. `GraphTraversalReader` reads adjacency IDs, fetches each edge, checks target visibility through document records, projects all returned edge attributes and sorts/materializes all vertices and edges. `GraphVertexVisibility` already caches per-read visibility decisions; the problem is not an absence of any cache. Nevertheless, the count operation performs substantial result construction and transfer that its caller immediately discards. Neighbor queries similarly discard the returned edges. This is a concrete API/execution mismatch, not proof of the percentage of total graph time it consumes.

The redesign candidate is one traversal primitive with operation-specific projection/aggregation at the storage owner. A count should retain the bounded visited/frontier state and required authorization/corruption checks, without building, sorting or transmitting the full edge/vertex response. Do not weaken row visibility, lifetime binding, cycle handling, distinct counting, depth semantics, cancellation or work budgets. Changing the benchmark cases/dataset is not required to remove unused result materialization; changing its published contract still needs explicit owner approval.

[SurrealDB graph keys](https://github.com/surrealdb/surrealdb/blob/v3.2.4/surrealdb/core/src/key/graph/mod.rs) include record identity, direction, relation/target identities and prefix/suffix bounds. This confirms that a key-value engine can represent bounded adjacency access; it does not establish that SurrealDB avoids every subsequent record read. KeyLoad already has directional adjacency prefixes. Replacing ZoneTree or adding another graph storage authority is not justified by this source comparison.

## TimescaleDB: exclude irrelevant chunks and execute aggregates at the source

[chunk_append/exec.c](https://github.com/timescale/timescaledb/blob/2.30.2/src/nodes/chunk_append/exec.c) excludes chunks at startup/runtime and propagates applicable tuple limits to child plans. [vector_agg/exec.c](https://github.com/timescale/timescaledb/blob/2.30.2/tsl/src/nodes/vector_agg/exec.c) additionally demonstrates per-batch expression reuse over columnar data, under its own Timescale license.

The repository's `TimescaleTimeSeriesDriver` creates a hypertable and `(series, at)` index and executes range, count/sum/min/max and time-bucket SQL on the server. It does not configure compression/columnstore. Therefore that vector-aggregation implementation cannot be asserted to explain this benchmark. Compare KeyLoad's actual series/time bounds, partitions examined, rows decoded and aggregate location first.

## KurrentDB: cached credential data, explicit authorization, direct index-to-log lookup

[InternalAuthenticationProvider.cs](https://github.com/kurrent-io/KurrentDB/blob/v26.1.2/src/KurrentDB.Core/Authentication/InternalAuthentication/InternalAuthenticationProvider.cs) uses an LRU of credential/principal data and removes entries on password-cache reset; cached authentication still verifies the supplied credential. [AuthorizationGateway.cs](https://github.com/kurrent-io/KurrentDB/blob/v26.1.2/src/KurrentDB.Core/Services/AuthorizationGateway.cs) checks the resolved user's operation before publishing it onward, with a path for checks that complete synchronously. [IndexReader.cs](https://github.com/kurrent-io/KurrentDB/blob/v26.1.2/src/KurrentDB.Core/Services/Storage/ReaderIndex/IndexReader.cs) can resolve an index position, validate the stream and handle hash collisions without scanning the entire event history.

The repository's Kurrent driver uses one persistent gRPC client, multi-stream append for load batches and a one-event forward read. Compare complete operation work and actual persistence acknowledgements; do not attribute all event throughput to authentication or omit the required durable write work.

## Exact KeyLoad paths requiring redesign

1. `DatabaseAuthenticationHandler.HandleAuthenticateAsync` calls `DatabaseCredentialResolver.ReadAsync` for protected HTTP requests.
2. `CanonicalCredentialDispatch.AuthenticateAsync` calls `IUserGrain.AuthenticateAsync`; audited operation-owner-authorized endpoints use its verified subject without a second principal operation.
3. `UserGrain` loads the credential from storage once and reuses it. Business authorization remains a fresh persisted operation check.
4. Endpoints which consume principal metadata still use `ReadCurrentPrincipalAsync`, with its own request id/envelope and `GrainReadKind.CurrentPrincipal` operation through the connection.
5. `ConnectionGrain.Request(requestId)` obtains a different ordinary `IRequestGrain` identity for each operation. `RequestGrain` uses `PreferLocalPlacement` and asks for deactivation after completion. A request is individually addressable; it does not create a stateless worker pool.
6. Each request producer resolves the storage owner through the database grain, then invokes local runtime or a remote owner execution path. Typed payloads, linked cancellation and CQRS settlement pass through multiple boundaries.
7. `DocumentReadExecution.ReadDocumentAtCut` reuses its authenticated resource/lifetime pair within the same captured read cut, without introducing a cache.

The remaining request and transport boundaries require matched measurements. The diagnostic trace includes these paths, but parked-thread stack frequency must not be presented as running-CPU cost. Microsoft's [stateless worker documentation](https://learn.microsoft.com/en-us/dotnet/orleans/grains/stateless-worker-grains) defines one logical pool per grain id, with automatically created activations that are not individually addressable. Independently addressed requests therefore use ordinary grains; scalable background work uses stable pool identities.

## Applied changes and acceptance

The current implementation removes the duplicate principal metadata operation only from audited operation-owner-authorized endpoints, reuses the authenticated resource lifetime within a captured read, supports scalar range seeks, and shares scalar physical planning with non-scanning EXPLAIN. It adds an immutable-operation storage-routing decision, bounded exact top-K only when terminal fusion ordering permits truncation, and full/vertices/count projection over the same graph traversal. The graph benchmark driver requests its required output without changing its shared cases, dataset or schedule.

The request-grain execution and original concurrency tests remain. Credential rotation/revocation, current principal/resource policy, row visibility, typed identity isolation, cancellation, deduplication and joined cleanup remain required. Hybrid/global/graph vector consumers retain complete branch rankings; zero/subnormal fusion weights preserve their existing tie semantics. No SIMD kernel or normalized persisted representation is changed.

Focused operation tests, a strict solution build and separate clean local timings with matched active allocation captures qualify the individual changes. Full Linux functional, recovery, cluster and exact-once gates remain required for delivery. A released-image Benchmarks run supplies public measurements. Source comparisons and local captures do not establish competitive superiority, a cost-based optimizer, distributed serializability, power-loss durability or a new engine contract.

The earlier validator checkpoint passed 111 focused tests and reduced its matched one-million-call allocation from 5,427.766 to 624.003 bytes per call (88.5%). This is a validator allocation result, not a database throughput multiplier. Time-series chunk/partition pruning, prepared-plan invalidation and vector storage layout remain research directions, without an implementation or performance claim in this change.


## WAL publication, group commit and disk flush

[PostgreSQL 18 XLogFlush](https://github.com/postgres/postgres/blob/REL_18_0/src/backend/access/transam/xlog.c)
rechecks the flushed WAL position so a follower can be covered by another writer.
Its optional commit delay lets more transactions join a flush, with a latency
tradeoff. [KurrentDB 26.1.2 StorageWriterService](https://github.com/kurrent-io/KurrentDB/blob/v26.1.2/src/KurrentDB.Core/Services/Storage/StorageWriterService.cs)
considers queued flushable messages and elapsed flush time before requesting a
flush. These implementations distinguish a command from the flush covering it.

[Qdrant 1.17.1 update_worker](https://github.com/qdrant/qdrant/blob/v1.17.1/lib/collection/src/update_workers/update_worker.rs)
explicitly flushes the WAL before a waited update. Its
[flush_workers](https://github.com/qdrant/qdrant/blob/v1.17.1/lib/collection/src/update_workers/flush_workers.rs)
separately flush segments and only acknowledge WAL truncation through their
persisted version. A periodic segment flush therefore does not establish that a
waited update skips WAL persistence.

[TimescaleDB 2.30.2 copy.c](https://github.com/timescale/timescaledb/blob/2.30.2/src/copy.c)
uses tuple and byte limits for buffered multi-insert and groups heap insertion
while preserving per-row index and trigger work. This is a bulk-load path, not
permission to acknowledge individual writes before their selected durability
boundary.

KeyLoad retains ordered group commit, native MVCC atomicity, per-command outcomes
and actual copy acknowledgements. ProcessDurable uses the synchronous ZoneTree
2.1.1 WAL publication contract, including Shared WAL for transactions, with
periodic disk sync owned by the engine. Explicit disk sync is separate. Current source still
requires its own process-recovery, full solution, replicated SDK/MCP and released
Linux benchmark qualification; native diagnostics establish no competitive gain.
