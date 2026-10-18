# Benchmark methodology

KeyLoad is compared with other databases by running the same workload against each
database's own native topology and reporting what was measured. This page explains
the rules behind that comparison and how to read the published numbers. The
execution contract (jobs, phases, schedules) is in
[Methodology](../Features/BenchmarkComparisons/Methodology.md); requirements and
acceptance criteria are in [BenchmarkComparisons](../Features/BenchmarkComparisons.md),
[ADR-122](../ADR/ADR-122-native-benchmark-methodology.md) and
[ADR-142](../ADR/ADR-142-shared-live-benchmark-sessions.md). The measured figures
themselves are on the [benchmark website](https://www.keyload.cloud/#benchmarks).

## Principles

- Every database runs the same deterministic scenario: the same corpus, seed,
  payload bytes, operation order and count, concurrency, timing boundaries and
  correctness oracle. An adapter executes the shared scenario; it never swaps in an
  easier workload.
- Each database runs its real native topology: one node and three nodes, with real
  membership. Client counts and independent standalone databases are never relabelled
  as cluster nodes. Only free community features are used, with no enterprise images
  or paid cluster features.
- Different databases and topologies never share a runner, process, container,
  volume or measurement session. Each runs in its own isolated Linux job.
- A capability a database does not provide natively is an explicit unavailable
  result, never a substitute or a zero.
- Acknowledgement and durability are compared as the contract each database really
  gives. Unequal contracts are visible in the report and rule out a performance
  winner claim.
- Published numbers come only from authenticated original JSON produced by GitHub
  Actions runs of the [Benchmarks workflow](../../.github/workflows/benchmarks.yml).
  Local runs are development evidence and are never published. Failed, skipped or
  missing cells stay null with their provenance and never refresh published figures.

## What is measured

Each job starts the database through Aspire, waits for readiness and membership, and
then runs separate real TUnit tests with the real client of that database: the
initial load, native index construction, sequential and random reads, search and
queries, and a mixed concurrent test. Setup, readiness and the final correctness
check (full content and replica verification) are outside operation latency.
Acknowledgement defines when a write is complete; no extra read is added to a
measured write. A native batch records its real size and duration and is never split
into invented per-record samples.

## Workload families and oracles

| Family | Correctness oracle |
|---|---|
| Point read | The returned document is compared semantically with the stored one (JSON key order may differ) |
| Document write | Unique create; every created document is read back after the timed interval |
| Exact vector search | The result must match an exhaustive float64 cosine oracle including top-K order and the projected document; approximate search forms its own cohort and reports recall@k |
| Queue cycle | enqueue, lease or receive, then acknowledge; missing, unknown, duplicate or mismatched completions fail the case, and the complete expected set is checked across concurrent workers |
| Graph neighbors and traversal | Sorted distinct reachable IDs, excluding the start vertex, checked against an independent breadth-first oracle on the same directed graph |
| Time series | Identical UTC samples for every database; only verified results count |

Protocol, projection and transport costs differ between databases (for example a
graph traversal that returns complete vertices versus IDs only); the report keeps
them visible instead of hiding them.

## Reading the results

- Percentiles use the nearest-rank method over all attempts, including timeouts and
  failures. They are never averaged across repetitions, and no best repetition is
  chosen.
- Useful throughput is the number of verified successes divided by the measured wall
  time. The wall time of a concurrent mixed test is not the sum of its lanes.
- The bytes field is the input document size, not estimated network traffic.
- Client CPU, allocations and working set describe the load generator. Database-node
  CPU and memory are separate observations; when they are not instrumented the cell
  says so.
- The load model is closed loop. It does not correct coordinated omission and does
  not qualify overload, endurance or power-loss behaviour.
- Every report records the exact source revision, run and job, runtime and CPU, image
  digests, engine versions, topology, resource limits, read consistency, index
  settings and effective acknowledgement and durability.

## Topology and guarantees

KeyLoad's one-node arm keeps a single copy. Its three-node arm is a replicated
cluster of three copies in which a write is acknowledged once a majority of the
copies confirm durable local apply. Other databases run their own native
configuration (for example synchronous commit, append-only fsync or quorum queues
where the engine offers them), and the exact settings are part of each report. A node
count alone does not establish equal guarantees.

Nothing here claims production readiness, power-loss durability or leadership over a
competitor; those claims need their own fault, endurance and exact-source
comparison evidence.

Primary sources used for the adapters: the first-party
[Aspire database integrations](https://aspire.dev/integrations/databases/qdrant/qdrant-host/),
the [pgvector exact-search contract](https://github.com/pgvector/pgvector),
[RabbitMQ acknowledgements and confirms](https://www.rabbitmq.com/docs/confirms) and
the [Neo4j Query API](https://neo4j.com/docs/query-api/current/query/).
