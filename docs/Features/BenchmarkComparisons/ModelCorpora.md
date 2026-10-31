# Model-specific comparison corpora

KeyLoad participates in every supported data-model comparison. An engine's
marketing category is not its workload capability. In particular, the
`Multi-model` label must not exclude KeyLoad from document, relational, key/value,
graph, vector, search, queue, event or time-series comparisons.

## Comparison boundary

Each workload family owns one canonical deterministic corpus, operation schedule
and independent result oracle. Every participating engine, including KeyLoad,
executes exactly that family's inputs and operations. Different families have
separate namespaces, preparation, state, measurements and correctness outcomes.
Document measurements cannot supply key/value, relational or other model results.

The existing engine/topology jobs and released-image deployment remain the
execution boundary. A live database can serve several families, but each family
gets its own corpus and run-owned namespace. This does not authorize a product
image build, Release invocation, product qualification dependency or website
design change.

Baseline cells use one and three nodes where the engine supports that topology,
1,000,000 initial retained records where applicable, at least 100,000 measured
operations per applicable case and the same 16-client schedule within a family.
An empty queue starts at zero retained messages and performs the same 100,000
confirmed message cycles on every queue engine. Graphs report vertex and edge
counts separately. Preparation and index construction have their own timing;
they are not silently included in request latency or reused from another family.

## Families and native execution paths

| Family | Shared data and operations | Existing owning paths to reuse |
| --- | --- | --- |
| Documents | Deterministic JSON documents; bulk ingestion, point/range reads, updates, deletes, mixed operations and final readback | `Documents/`, document targets and the current document session |
| Key/value | Deterministic keys and equal JSON value bytes in a separate namespace; put/get/replace/delete and mixed operations | Native Redis commands and KeyLoad's public document-by-key API; separately measured from documents |
| Relational | Identical typed rows, schema, predicates and mutation schedule; insert, key/range selection, update/delete and mixed operations | Relational targets and KeyLoad's typed-table/SQL APIs; a document table is not evidence of KeyLoad typed-row performance |
| Graph | Identical vertex identities, directed edges, fan-out, start vertices and depth; ingestion, neighbors and traversal | Existing `GraphNeighbors`/`GraphTraverse` adapters and independent reachability oracle |
| Vectors | Identical float32 vectors, payloads, query inputs, metric and top-k; ingestion and exact cosine queries | Existing vector adapters and bounded scalar exact oracle; ANN is a distinct workload |
| Full-text search | Identical text, token/query semantics and expected document sets; ingestion/index build and search | Native text-search APIs; ID/numeric predicates are not full-text measurements |
| Queues | Identical message identities/bodies and client schedule; confirmed publish, receive and manual ACK | Existing `QueueCycle` adapters and exact-once accounting within the measured run |
| Events | Identical streams, event identities/revisions/bodies and append/read schedule; preparation, append, read and concurrent append/read | Existing stream adapters and acknowledged-event readback oracle |
| Time series | Identical UTC timestamps, series identities, tags, values and windows; ingestion, range/latest reads and aggregates | Existing persistent time-series targets, workload and independent numeric oracles |
| Analytics | Identical typed facts, grouping/filter semantics and expected numeric results | Actual server-side analytical APIs; client aggregation cannot be reported as database work |
| Blobs/files | Identical object identities/bytes and ranges; upload, whole/range read and deletion | Actual public storage contracts; native API availability must be confirmed before enabling a comparison |

Only engines with an actual equivalent native operation participate in a family.
KeyLoad must execute each available native family itself. Missing native APIs are
reported as explicit implementation blockers, never hidden with client-side
emulation, borrowed document numbers, fabricated measurements or a green
all-unavailable family.

## Required evidence

- A family identifier binds every case to its corpus and operation schedule.
- Corpus evidence retains the seed, actual record shape/counts, payload/vector or
  graph/time-series parameters and deterministic content fingerprint.
- The schedule retains operation counts, selected identities/predicates, mutation
  groups and client concurrency. A mixed case proves overlapping lanes.
- Aggregation compares corpus and schedule identities inside a family across all
  engines and topologies before comparing numbers.
- Every measurement retains actual attempts, successes, failures, elapsed time,
  per-operation observations, acknowledgement contract and independent result
  verification. Data/resource/provenance failures remain failures.
- Reports expose KeyLoad in each applicable family, with that family's own
  measurements. There is no overall performance winner assembled from different
  models or a static one-category-per-engine filter.
- Public figures still come only from the latest completed successful full
  Benchmarks run and its original artifacts. A newer implementation never creates
  missing historical measurements.

## Current implementation boundary

The thirteen-case session adds exact vectors, queues and events to the seven
document cases. It does not complete the family inventory above: graph execution
is not connected to that session, key/value and typed relational corpora are not
separately measured there, and native search/time-series/analytics/blob coverage
must be connected or have an exact native-API blocker recorded. One shared native
model corpus/namespace also does not establish independent family execution for a
multi-model engine. This remaining work is required before claiming a complete
multi-model comparison.
