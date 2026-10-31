# Benchmark methodology

KeyLoad is compared with other databases one data type at a time: documents,
key-value, relational, graph, vector, search, time series, analytics, queues and
events. This page explains how to read the published numbers. The exact contract
(types, cases, databases, result files and how to run one cell) is in
[BenchmarkComparisons](../Features/BenchmarkComparisons.md); the decision is
[ADR-150](../ADR/ADR-150-per-type-benchmark-jobs.md). The figures are on the
[benchmark website](https://www.keyload.cloud/#benchmarks).

## How a number is produced

- Every database of a data type runs the same standard dataset and the same
  scenario: seed 1729, 1,000,000 records, 16 concurrent clients and 100,000
  measured operations per case. KeyLoad runs every type with the same dataset and
  scenario as the specialist databases.
- Each database, data type and node count is one GitHub Actions job on its own
  Linux runner. Aspire starts the database from its pinned image; KeyLoad runs its
  latest released server image, pinned by digest for the whole run.
- The cases of a type run in order on that one live database, starting with the
  measured load of the dataset. Each database is reached through its own .NET
  client or HTTP API.
- Every answer is checked: the stored document or row, the ids of a range, a
  vertex's neighbours, the exact cosine top-10, the documents matching a search,
  series aggregates and windows, exactly-once queue delivery and the events of a
  stream. A wrong answer counts as an error and fails the case.

## Reading the results

- **Operations per second** is successful operations divided by the case's wall
  time; a load counts records per second. The search index build is one call and
  shows seconds instead.
- **p50, p95, p99** are nearest-rank latencies per call over successful calls. A
  load sends records in batches, so its latency is per batch; the recorded-values
  table shows the units per call.
- **3 nodes** runs the database's three-node deployment where one exists. KeyLoad
  runs three silos keeping three copies; a write is acknowledged after every copy.
  Other databases use the replication settings of their deployment, so the same
  node count does not mean the same guarantees.
- **Failed** means the case ran and failed; the real error is shown. **No data**
  means no result exists for that cell. **Single node** means the database has no
  three-node deployment here. No number is estimated or filled in.

## What the numbers do not show

The figures describe one Benchmarks run on GitHub-hosted runners whose hardware is
not matched between jobs. They are not production readiness, power-loss durability,
endurance or overload results, and they do not make KeyLoad or any other database a
winner. The load is a closed loop of 16 clients and does not correct coordinated
omission. KeyLoad writes each job's data into one partition, so its three-node
numbers measure replication, not partition fan-out.
