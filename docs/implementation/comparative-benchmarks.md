# Comparative benchmark kit

The Aspire comparison profile runs the production RF3 KeyLoad topology and pinned PostgreSQL/pgvector, Qdrant, RabbitMQ and Redis containers. It runs each engine sequentially with the same deterministic corpus, JSON bytes, float32 embeddings, request schedule, warmup, concurrency and repetitions. It measures the remote command path rather than embedded microbenchmarks.

## Run

Docker must be running. From the repository root:

```sh
dotnet restore KeyLoad.slnx --locked-mode
dotnet run -c Release --project src/KeyLoad.AppHost -- --Benchmarks:Enabled=true
```

The Aspire dashboard shows three KeyLoad voters, the four external engines and `comparisons`. The runner starts after resources become healthy, performs the suite, prints results and exits with code 0 only when every supported case passes correctness. The AppHost stays open so logs and the dashboard remain available; stop it normally when finished. Each invocation uses a new cluster directory and report directory under `artifacts/comparisons/<run>/`, separate from the normal `data/cluster` profile. External containers belong to this AppHost lifecycle.

All engine data lives in isolated host directories: `cluster/` for KeyLoad and `external/<engine>/` for container bind mounts. `Benchmarks:DataRoot` overrides that run root. This makes storage ownership visible, retains files for inspection after a manual run and keeps database files outside the Docker VM's image/cache filesystem. Docker Desktop bind mounts still have a different filesystem path from native host processes; reports record that cost. The automated smoke removes its own directories after stopping Aspire.

Configuration is forwarded from the AppHost to the runner:

```sh
dotnet run -c Release --project src/KeyLoad.AppHost -- \
  --Benchmarks:Enabled=true \
  --Benchmarks:Documents=1000 --Benchmarks:Operations=1000 \
  --Benchmarks:PayloadBytes=1024 --Benchmarks:Dimensions=32 --Benchmarks:TopK=10 \
  --Benchmarks:Concurrency=8 --Benchmarks:Warmup=50 --Benchmarks:Repetitions=3 \
  --Benchmarks:Seed=1729 --Benchmarks:TimeoutSeconds=30 \
  --Benchmarks:Output=artifacts/comparisons/my-run/reports
```

`Benchmarks:SourceRevision` optionally records the source commit. Local uncommitted changes still need their own provenance when sharing results. Full report paths are printed by `comparisons`.

## Shared scenarios

| Scenario | KeyLoad | PostgreSQL + pgvector | Qdrant | RabbitMQ | Redis |
|---|---|---|---|---|---|
| PointRead (B01 subset) | document JSON | JSONB by primary key | unsupported | unsupported | string key |
| DocumentWrite (B02 subset) | create with expected revision 0 | INSERT, primary-key uniqueness | unsupported | unsupported | SET NX |
| VectorExact (B05 subset) | cosine exact + full document projection | cosine exact, no ANN index + JSONB projection | exact=true, HNSW disabled + document payload | unsupported | unsupported |
| QueueCycle (E03 subset) | enqueue → fenced receive → ACK | enqueue → SKIP LOCKED lease → fenced ACK | unsupported | confirmed persistent publish → basic.get → manual ACK + RPC barrier | unsupported |

Every supported row has the same operation count and input schedule. Unsupported cases retain null measurements. Writes are unique creates and are read back after timing. Point reads compare JSON semantically because JSONB can reorder keys. Exact vector results must match the exhaustive float64 cosine oracle, including top-K ordering and document payload; approximation does not silently enter the exact cohort. Queue validation rejects missing, unknown or duplicate completions and mismatched bodies. Each concurrent worker may consume another worker's message; the complete expected set is checked across workers.

The source DDL and prepared SQL are in `PostgresTarget.cs`. PostgreSQL uses pooled worker connections and Npgsql automatic preparation, a JSONB document table with a B-tree primary key, vector columns without ANN indexes, a ready-state queue index and an atomic claim statement. Claims end before processing/ACK; ACK matches the lease owner and deadline. Queue retention and dedup metadata differ between engines and are reported as exploratory overhead, not normalized away.

## Results and interpretation

`results.md` sorts observed useful operations/s within each scenario and repetition. `results.json` contains options, corpus SHA256, runtime/OS/CPU metadata, engine versions, image digests, contracts, failures, stage percentiles and raw attempt samples. `samples.csv` retains every measured attempt with worker, start/completion time, error code, payload bytes, completed message ID and enqueue/receive/ACK times. The bytes field describes input document bytes, not estimated network traffic. Percentiles use nearest rank over all attempts, including timeouts. Useful throughput counts verified successes divided by the measured wall time; QueueCycle successes must have unique completed message IDs.

Queue stage percentiles cover cycles that returned all three stage timings. A failed/incomplete cycle still remains in overall latency and the raw samples, with null stage timings.

Setup, corpus/oracle generation, warmup, JSON correctness checking and write readback are outside timings. Queue operations include all three round trips/stages. Repetitions stay separate and engine order rotates. The current load model is closed loop; it does not correct coordinated omission or qualify overload behavior. A short smoke timing is adapter evidence, not a performance claim.

Topology and guarantees are deliberately visible:

| Engine | Current development configuration |
|---|---|
| KeyLoad | RF3, three host processes, one physical shard; QuorumProcessDurable and strong quorum reads; root authentication/authorization |
| PostgreSQL | one Docker primary; verified fsync=on and synchronous_commit=on; owner role, no RLS; no synchronous replicas |
| Qdrant | one Docker node, replication_factor=1; static seeded collection, wait=true on ingestion; API key |
| RabbitMQ | one Docker broker, one-member quorum queue; persistent messages and publisher confirms; manual ACK plus same-channel RPC barrier |
| Redis | one Docker server; verified appendonly=yes and appendfsync=always; primary reads and Aspire authentication |

These are shared-workload observations with different resource, replication, security and transport costs. They cannot establish an equal-durability winner. All services share one development host; Docker Desktop adds a VM path for external databases, while KeyLoad runs on the host. Independent failure domains, CPU/RAM/disk budgets and power-loss guarantees are unqualified. RabbitMQ consumer ACK has no server acknowledgement; the added RPC barrier measures broker processing, not an independently proven durable ACK receipt.

## Automated proof and remaining gates

```sh
dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore -c Release
dotnet test --project tests/KeyLoad.ComparisonTests --no-build --no-restore -c Release
```

The comparison test owns one Aspire lifecycle and runs the real `comparisons` process across all five engines, with 32 documents, 12 operations, two repetitions and two workers. It checks adapter correctness, RF3 enforcement, pinned images, report contents and complete CSV accounting. CI runs this Docker-backed smoke on Linux and uploads `artifacts/comparisons/smoke/`. Existing unit, recovery and RF3 failure suites remain independent required checks.

For an automated exploratory run that exits after the suite, set test overrides such as `KEYLOAD_COMPARISON_DOCUMENTS=256`, `KEYLOAD_COMPARISON_OPERATIONS=100`, `KEYLOAD_COMPARISON_REPETITIONS=3`, `KEYLOAD_COMPARISON_CONCURRENCY=8`, `KEYLOAD_COMPARISON_DIMENSIONS=32`, `KEYLOAD_COMPARISON_TOPK=10` and `KEYLOAD_COMPARISON_REPORT_NAME=exploratory` before invoking the comparison test. These options retain correctness assertions and save reports under the selected artifact directory. Long capacity/endurance runs use the AppHost directly; the smoke harness has an eight-minute deadline.

KL-006, KL-073 and KL-101 remain in progress. Matched PostgreSQL synchronous RF3 and RabbitMQ RF3 profiles, resource budget measurements, non-owner/RLS/masking, common application endpoints, graph/time-series/filtered ANN fixtures, open-loop offered load, larger-than-RAM data, Marten/Wolverine atomic workflow, optional KurrentDB stream/subscription baselines and endurance/fault qualification remain planned. No comparative gain or production-readiness gate is closed by this kit.

Implementation follows the first-party [Aspire database integrations](https://aspire.dev/integrations/databases/qdrant/qdrant-host/), [pgvector exact-search contract](https://github.com/pgvector/pgvector), and [RabbitMQ acknowledgements and confirms](https://www.rabbitmq.com/docs/confirms). Product methodology is defined in architecture v0.3 sections 32 and 45.
