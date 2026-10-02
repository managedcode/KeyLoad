# TimeSeries and Timescale comparison acceptance

## Goal and visible outcome

The GitHub comparison workflow can exercise KeyLoad time-series operations on its real RF3 Aspire cluster, TimescaleDB's persistent hypertable/time-bucket operations, and the owner's ManagedCode.TimeSeries aggregation library over one deterministic UTC sample set. Reports clearly identify their different persistence and acknowledgement guarantees.

Brainstorm: [timeseries-comparison.brainstorm.md](timeseries-comparison.brainstorm.md). Product feature: [TimeSeries](docs/Features/TimeSeries.md). Comparison owner: [BenchmarkComparisons](docs/Features/BenchmarkComparisons.md).

## Scope and boundaries

In scope: centrally pin the published ManagedCode.TimeSeries package; start a digest-pinned TimescaleDB container through Aspire benchmark mode; use a per-run database/schema and hypertable; exercise KeyLoad through its real .NET SDK and RF3 endpoints; compare TimescaleDB using Npgsql; use ManagedCode.TimeSeries for an in-memory bucket aggregation arm; add meaningful TUnit contracts and real Aspire comparison cases; preserve raw evidence and report guarantee metadata.

Out of scope: changing KeyLoad's public sample API or persisted format; replacing KeyLoad storage with an in-memory package; changing the existing nine-engine/schema3 support matrix or its counts; claiming equal durability, replication, acknowledgement, or a performance winner; local test/benchmark execution; frontend changes before qualified evidence exists.

## Actors and entry points

Actors: GitHub TUnit runner, Aspire AppHost, three KeyLoad RF3 nodes, comparison host, KeyLoad .NET SDK, Npgsql Timescale client, and the ManagedCode.TimeSeries library target. Entry point is the existing comparison workflow and its benchmark-mode AppHost. The official MCP SDK is not extended because this task adds no product API; existing MCP qualification remains required by the overall project gate.

## Requirements and acceptance criteria

- **REQ-TSC-001 — Aspire resource and pinned identity.** **AC-TSC-001 pass:** benchmark mode starts a distinct TimescaleDB resource using `timescale/timescaledb:2.30.2-pg18` pinned to multi-platform manifest `sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e`; a database endpoint/secret is obtained through Aspire references; readiness is checked before the runner starts; normal non-benchmark topology does not start this resource. The container is isolated and ephemeral with no cross-run data volume; no durability beyond that container's lifetime is claimed. **Fail:** mutable image tag, hardcoded endpoint/credential, or implicit startup in ordinary RF3 tests.
- **REQ-TSC-002 — Matched persistent operations.** **AC-TSC-002 pass:** one deterministic sample dataset (stable IDs, UTC timestamps, numeric values, series identity and tags) is appended/read through KeyLoad's real SDK against RF3 and inserted/read/aggregated through TimescaleDB's Npgsql connection. Exact readback and aggregate outputs agree with an independent oracle for ordered, duplicate, offset-normalized, inclusive-boundary, empty-range, and error cases. Setup/seed/readback are outside measured operations. **Fail:** fabricated data, omitted failed operations, changed sample payload, boundary mismatch, or use of an in-memory KeyLoad substitute.
- **REQ-TSC-003 — ManagedCode.TimeSeries use.** **AC-TSC-003 pass:** the centrally pinned, published `ManagedCode.TimeSeries` 10.0.0 package processes the same UTC samples with a bucketed accumulator/summer; bucket keys and declared aggregate values match the oracle at boundaries and for out-of-order input. Every report labels this arm as an in-memory library primitive with no persistence/recovery/replication guarantee. **Fail:** hand-written replacement aggregation, hiding that the library is in-memory, or describing it as a database.
- **REQ-TSC-004 — Isolated report contract.** **AC-TSC-004 pass:** time-series results use a distinct profile/report contract with source SHA, workload hash, exact image/package versions, operation timings, every attempted result, correctness failures and explicit storage/acknowledgement guarantees. The existing nine-engine support counts and schema3 publication requirements remain byte/semantically unchanged. **Fail:** merging unlike guarantees into one winner score or changing existing matrix counts to accommodate this profile.
- **REQ-TSC-005 — Resource ownership and failure behavior.** **AC-TSC-005 pass:** each run owns a unique Timescale schema/database namespace and marker; cleanup can remove only the positively acknowledged owned namespace; failed creation/response cannot authorize deletion; retries do not duplicate or alter seeded values; cancellation bounds startup, operations, and cleanup. **Fail:** cross-run cleanup, credential/body leakage, unbounded wait, or dropped cleanup error.
- **REQ-TSC-006 — Qualification.** **AC-TSC-006 pass:** target comparison tests, full Release build, format, governance, TUnit, recovery, and RF3 SDK/MCP qualification run through the canonical GitHub workflow at the delivered SHA with no skips; raw test and comparison artifacts identify that SHA. **Fail:** local tests/benchmarks counted as qualification or the profile marked qualified without its actual successful GitHub artifacts.

## Test matrix

| AC | Automated proof | Level and assertions | Verification |
|---|---|---|---|
| AC-TSC-001 | Aspire benchmark resource model test and real startup readiness assertion | Static model + GitHub Docker; exact image digest, benchmark-only registration, resolved reference | `dotnet build KeyLoad.slnx --no-restore --configuration Release`; full `ci.yml` |
| AC-TSC-002 | Time-series comparison TUnit flow against RF3 SDK and real Timescale hypertable | Docker/Aspire public flow; stable roundtrip, ordering, idempotency, range edges and exact aggregates | GitHub `KeyLoad.ComparisonTests` comparison job; retain logs/artifact |
| AC-TSC-003 | ManagedCode.TimeSeries target contract test using the same generated dataset | TUnit; exact bucket/value oracle and explicit non-durable capability metadata | GitHub `KeyLoad.ComparisonTests` |
| AC-TSC-004 | Report contract/source-manifest tests and existing nine-engine regression assertions | TUnit/static; separate report schema and unchanged existing counts | GitHub comparison job and complete CI |
| AC-TSC-005 | Real resource ownership, duplicate-run and cleanup-failure cases | TUnit against Aspire-owned Timescale container; only acknowledged private schema is removed | GitHub comparison job |
| AC-TSC-006 | Complete canonical workflow and downloaded exact-SHA artifacts | All mandatory unit/recovery/RF3/.NET/MCP/comparison/format/governance gates | `gh workflow run ci.yml --repo managedcode/KeyLoad --ref main`; retain run/job URLs and artifacts |

Manual evidence is limited to reviewing the generated report's guarantee labels and the exact-image metadata; it does not replace automated assertions. UI publication is N/A until successful CI evidence exists. Product MCP/API changes are N/A because no database contract changes.

## Migration and rollback

No KeyLoad persisted data or public API migration is needed. Existing comparison schema3 results remain immutable and untouched. Rollback disables the new isolated profile and removes only its package/resource/report/test code; it does not remove historical results or alter the other nine engines. The published ManagedCode.TimeSeries package remains centrally pinned only while consumed by this comparison arm.
