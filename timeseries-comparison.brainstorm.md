# TimeSeries and Timescale comparison brainstorm

## Problem

The current comparison harness has a general PostgreSQL baseline, but it has no TimescaleDB service or time-series-specific comparison. KeyLoad already persists and reads ordered samples through its public .NET SDK. The owner also requires the ManagedCode.TimeSeries library to be used for this workload.

## Constraints and facts

- Preserve the existing nine-engine/schema3 benchmark matrix and its support counts.
- KeyLoad comparisons keep the real Aspire RF3 cluster and real SDK client.
- TimescaleDB is a persistent PostgreSQL extension and must run as its own digest-pinned Aspire resource.
- ManagedCode.TimeSeries 10.0.0 is published for .NET 10. Its summers/accumulators are in-memory bucket primitives; they do not provide persistent range-query or replication guarantees.
- Runtime comparison and qualification happen only in GitHub Actions. Do not claim local tests or measurements.
- Use the identical UTC-normalized generated sample set and a shared exact aggregation oracle. Report persistence and acknowledgement guarantees separately.

## Options considered

1. Reuse the general PostgreSQL/pgvector comparison target. Rejected: it cannot prove Timescale hypertable and time-bucket behavior, and would blur the existing general PostgreSQL baseline.
2. Replace KeyLoad's persistent sample implementation with ManagedCode.TimeSeries. Rejected: the library's in-memory buckets do not implement KeyLoad's persisted idempotency, authorization, range, RF3, or recovery contracts.
3. Add an isolated TimeSeries comparison profile with three explicitly distinct arms: KeyLoad RF3 through its SDK, TimescaleDB hypertable through Npgsql, and ManagedCode.TimeSeries as an in-memory aggregation primitive. Chosen: it tests the actual requested products without changing the nine-engine matrix or making false persistence-equivalence claims.

## Chosen direction

Add the TimescaleDB container only to the Aspire benchmark topology, pin its image by manifest digest, and expose its connection through Aspire references. Add a separate time-series profile/report whose identical deterministic samples exercise KeyLoad append/range-read, Timescale insert/range/`time_bucket` aggregation, and ManagedCode.TimeSeries aggregation. Keep initialization/readback outside timed phases, retain every failed attempt, and stamp the result with exact image/package versions, source SHA, scenario, and each arm's storage/acknowledgement contract.

## Risks and mitigations

- Different persistence guarantees can make latency comparisons misleading. Keep operation phases and guarantees separate and prohibit one overall winner claim.
- Bucket alignment can differ. Use fixed UTC timestamps, one-second buckets, and an independent exact oracle with before/at/after boundary samples.
- Container startup and test cost can inflate normal integration suites. Start the resource only in benchmark mode and use the existing GitHub comparison workflow.
- A benchmark-only dependency can be mistaken for product runtime integration. Name and document the ManagedCode.TimeSeries arm as an in-memory library primitive; KeyLoad's persistent public API stays unchanged.

## Open questions

None block implementation. Version and image are pinned from the published ManagedCode NuGet package and official Timescale Docker metadata reviewed on 2026-10-02.
