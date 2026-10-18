# ClusterRouting: Orleans execution primitives

Decision: [ADR-110](../../ADR/ADR-110-native-orleans-execution-primitives.md). Cluster concept: [ADR-136](../../ADR/ADR-136-native-orleans-partition-ownership.md). Connection grain: [ADR-125](../../ADR/ADR-125-connection-owned-execution.md).

This specification states which native Orleans 10.4 primitive KeyLoad uses for each kind of work, and how each choice is verified. Frontend and client schemas are N/A: the public SDK, MCP and SQL operations are the entry points for verification.

## Primitives

| Work | Primitive | Key and identity |
|---|---|---|
| A connected client's operations | `ConnectionGrain`, `[Immovable]`, `[PreferLocalPlacement]` | Server-issued connection id |
| Commands of one logical partition | Partition grain | Canonical partition id from the shared ID helper |
| A node's ZoneTree files | Storage host DI singleton (lifecycle participant) plus one storage Grain Service per silo | One per silo; remote callers use `GetGrainService(SiloAddress)` |
| Copies of a write | Grain Service calls to the target silos | Target silos from `IClusterMembershipService` |
| Queue deadlines, due and recurring work, remote queue transfers, ANN projection, time-series chunks | `[StatelessWorker(maxLocalWorkers, removeIdleWorkers: false)]` grain with a `RegisterGrainTimer` flush | One grain key per queue, index or series from the shared ID helper |
| Durable periodic work (≥ 1 minute) | Reminders | Owning grain |
| One-time due work | Durable Jobs with idempotent `IDurableJobHandler` | Target grain |
| Streamed results | `IAsyncEnumerable<T>` grain methods | Call-scoped |
| Per-call identity | `RequestContext` via ManagedCode.Orleans.Identity | Verified subject only |

```mermaid
flowchart LR
    Api[SDK MCP SQL] --> Conn[ConnectionGrain]
    Conn --> Part[Partition grain]
    Part -->|Orleans call| Svc[Storage Grain Service on a chosen silo]
    Svc --> Host[Storage host singleton]
    Host --> Zt[(ZoneTree)]
    Ev[Events from apply] --> Wk[StatelessWorker buffer]
    Wk -->|timer flush| Part
```

## Connection and request lifetimes

One Orleans grain per accepted client connection runs that connection's independent operations in parallel within typed bounds (ADR-125). Each operation keeps its own signed request id, fresh persisted authorization, cancellation and cleanup. No grain is created per request and no completed-request history is kept. Disconnect or idle expiry stops admission, joins the original operations and requests `DeactivateOnIdle`.

## Background work

Background work never checks leadership. A stateless worker accepts events, buffers them in memory and, on an Orleans grain timer, writes the batch through the normal partition grain → ZoneTree owner path. The flush interval is a typed option (for example 5 seconds). The buffer is lost on a silo crash, so a worker buffers only work that can be rebuilt from committed ZoneTree records from a persisted watermark; clients are acknowledged only after the authoritative commit.

## Requirements and acceptance

| Requirement | Acceptance |
|---|---|
| REQ-ORL-001: use the primitive listed above for each kind of work; do not add custom schedulers, coordinators, leader election or transports. | AC-ORL-001: a source review lists every grain, Grain Service, stateless worker, timer, reminder and durable job with the work it owns; none checks leadership or opens its own transport. |
| REQ-ORL-002: stateless worker pools stay bounded. | AC-ORL-002: under saturation the number of local activations per key never exceeds `maxLocalWorkers`, buffered items and bytes stay within their typed limits, and the overflow path is an explicit refusal. |
| REQ-ORL-003: `[OneWay]` carries only advisory, loss-tolerant signals. | AC-ORL-003: dropping or duplicating such a signal changes no committed state and the affected operation still completes. |
| REQ-ORL-004: interleaving is enabled only where the invariant allows it. | AC-ORL-004: a long operation on a connection does not block close or cancel (`[AlwaysInterleave]`); concurrent operations keep separate identity and authorization. |
| REQ-ORL-005: durable jobs and reminders have idempotent effects. | AC-ORL-005: a job retried after a handler failure or a provider restart produces the effect exactly once or an explicit no-op. |
| REQ-ORL-006: efficiency claims need measurements. | AC-ORL-006: any performance claim for a primitive choice is backed by matched Linux benchmark runs. |
| REQ-ORL-007: ZoneTree is opened and closed by the storage host through the silo lifecycle, not in `GrainService.Stop()`. | AC-ORL-007: silo stop, cancelled stop and restart release the file locks once and reopen the same files with all acknowledged data. |
| REQ-ORL-008: Orleans messaging is not database history. | AC-ORL-008: committed events and queue messages are read from ZoneTree, never from Orleans streams, observers or broadcast channels. |
| REQ-ORL-009: only `ITransactionalState` participates in Orleans transactions. | AC-ORL-009: no document claims cross-partition ACID from Orleans transactions; same-partition atomic batches remain the atomicity contract. |
| REQ-ORL-010: identifiers come from one shared helper per identity kind. | AC-ORL-010: case-variant or non-canonical keys are rejected at routing and storage boundaries; the helper round-trips canonical keys unchanged. |
| REQ-ORL-011: background work uses stateless workers with timer flush and no leadership check. | AC-ORL-011: events produced by committed apply are flushed within the configured interval; a silo crash between buffer and flush loses no acknowledged data and the work is rebuilt from the persisted watermark. |
| REQ-ORL-012: native Orleans telemetry is exported with fixed, privacy-safe metadata. | AC-ORL-012: exported spans, metrics and logs carry no credentials, payloads or unbounded labels and keep trace parentage across grain calls. |
| REQ-ORL-013: one connection grain owns its client's operations. | AC-ORL-013: repeated operations from one connection reuse one activation; independent principals and commands stay isolated; disconnect removes the activation. |

## Verification

Real TUnit operations through the .NET SDK and official MCP client against the Aspire-owned Docker cluster, plus focused unit tests of the stateless-worker buffer and the ID helper. Existing tests that still exercise leader-gated due waits (for example `RecurringDueWaitTests`) describe the code being replaced and do not qualify AC-ORL-011.
