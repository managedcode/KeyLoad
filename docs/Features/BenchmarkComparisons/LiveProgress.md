# Live benchmark progress

Parent: [BenchmarkComparisons](../BenchmarkComparisons.md).
Decision: [ADR-085](../../ADR/ADR-085-live-benchmark-progress.md).

Measurement jobs share one live native database/topology across separate TUnit cases
([ADR-142](../../ADR/ADR-142-shared-live-benchmark-sessions.md),
[SessionExecution](SessionExecution.md)). A long native workload must not look idle in
GitHub Actions: workload phase and attempt progress are visible while it runs, and a
cancelled job retains the latest workload phase. This feature owns benchmark
observability only, not the database execution implementation. The dataset,
operation-count, native capability, correctness, privacy, cancellation, resource,
topology and provenance requirements stay mandatory; the seven document cases do not
complete the required vector, graph, queue, event, time-series or physical 100k-corpus
coverage, and exact-source native Linux qualification is a separate gate.

| Requirement | Acceptance | Automated proof |
|---|---|---|
| REQ-BC-LIVE-001 visible real progress | AC-BC-LIVE-001 report oracle, initialization, warmup, mutation preparation, measurement and validation transitions; emit a bounded heartbeat at most every 30s while active. Measurement counts represent actually settled attempts, with failures separate; initialization totals may remain unavailable. No fabricated throughput, ETA or success from progress | real ComparisonRunner regression and live child/relay tests |
| REQ-BC-LIVE-002 privacy and resource bounds | AC-BC-LIVE-002 emit only a fixed marker, closed phase, nonnegative repetition/count/elapsed values; lines at most 512 characters, no credentials, document content, endpoint, error text or arbitrary target names. Capture removes only the pinned native Aspire UTC timestamp prefix before validating the entire marker. Keep one latest snapshot, not an unbounded queue; logging exceptions cannot replace the original workload outcome | positive/negative parser and real file/process tests |
| REQ-BC-LIVE-003 cancellation evidence | AC-BC-LIVE-003 persist the latest accepted snapshot under the cell-owned failures directory while the test is running. Actions relays that file independently of TUnit/CLI buffering; always-upload teardown retains it after cancellation. Stop and join every progress observer, preserve native AppHost exit and signal cancellation to owned process group | genuine process exit/cancellation regressions; delivered Linux cancellation artifact |
| REQ-BC-LIVE-004 unchanged measurement | AC-BC-LIVE-004 retain exact vector semantics, corpus, repetitions, concurrency, per-operation deadlines, real clients, Docker/Aspire topology and report/provenance contracts. Oracle preparation checks caller cancellation before each bounded operation. Progress must not become website performance evidence | existing corpus/selection/report checks, real runner cancellation test and source review |

```mermaid
flowchart LR
    Native[ComparisonHost native workload] --> Snapshot[Closed bounded progress line]
    Snapshot --> Capture[TUnit native resource log capture]
    Capture --> File[Latest cell owned progress file]
    File --> Relay[Actions live output observer]
    File --> Artifact[Always retained failure diagnostics]
    Native --> Report[Unchanged original measurement JSON]
```

Slice map: Comparisons owns the observer/phase accounting; ComparisonTests owns native
log capture and the durable latest snapshot; scripts/BenchmarkComparisons owns the real
process/output relay; benchmarks.yml is the workload join and runs the focused native
logger/parser/file regressions. Benchmark contract cases live in
`tests/KeyLoad.ComparisonTests/BenchmarkComparisons/UnitContracts`; Build and Tests
unit/scalar execution never runs them. Database API, SDK/MCP, stored state, GUI and
website metrics: N/A.

Verification: Release solution build, Aspire-owned focused unit/comparison regressions,
formatter/governance, then delivered-source CI and native Benchmarks. Real
Docker/replicated-cluster/recovery and mandatory endurance/scale gates need their own
genuine runs. The profile of 4096 documents and 50000 attempts is a control workload,
not 100k/1m dataset qualification. Original-job times are not request latency
metrics. A source checkpoint is not a passing build, runtime, replication or
performance qualification.
