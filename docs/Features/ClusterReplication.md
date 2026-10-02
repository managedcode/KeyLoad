# ClusterReplication

Status: implementation in progress. Owner: KeyLoad lead. The owner's Orleans-only correction supersedes the DotNext candidate in architecture v0.3. Decision: [ADR-036](../ADR/ADR-036-orleans-foundation.md).

| Requirement | Acceptance and observable evidence |
|---|---|
| REQ-REP-001: Orleans carries votes, ordered appends, read barriers, forwarding and snapshot transfer. No DotNext runtime/package remains. | AC-REP-001: dependency/source inventory contains no DotNext reference; three Docker silos accept .NET SDK operations through request grains. |
| REQ-REP-002: terms, votes, log entries and commit positions are node-owned and process durable before acknowledgement. | AC-REP-002: real-process append/vote/commit interruption tests reopen the acknowledged prefix and reject complete-record corruption. |
| REQ-REP-003: an odd fixed RF3 voter set commits only with a majority; a current-term quorum barrier precedes public reads. | AC-REP-003: leader kill preserves acknowledged commands and stable-ID outcomes; an isolated minority rejects reads and writes. |
| REQ-REP-004: a restarted/empty replica catches up by a verified bounded snapshot followed by the ordered tail. | AC-REP-004: SDK-visible documents, topic/checkpoint/outbox/inbox outcomes survive replica erase/restart; interrupted/corrupt transfers preserve a complete recoverable cut. |
| REQ-REP-005: replicated principals and grants remain authoritative, including after leadership changes and snapshot catch-up. | AC-REP-005: SDK/MCP unauthorized and protected-field requests fail without canonical effects before and after failover. |
| REQ-REP-006: bounded anti-replay admission retains reserved capacity for votes, heartbeats, noop/current-term establishment and membership/lease control when application traffic saturates. | AC-REP-006: signed replay/tamper requests are rejected; saturated forward/read/data-append traffic is throttled without exhausting control capacity; real RF3 client load preserves leadership and minority denial. |

```mermaid
flowchart LR
    Request[Orleans request grain] --> Host[Node-local replica host]
    Host --> RPC[Orleans replica Grain Service]
    RPC --> Voters[Three durable voter logs]
    Voters --> Apply[Ordered canonical apply]
    Apply --> Outcome[Stable command outcome]
```

Slice map: `src/KeyLoad.Replication/Features/ClusterReplication/` owns protocol/log contracts and coordination; `src/KeyLoad.Orleans/Features/ClusterReplication/` owns the per-silo service/transport; `src/KeyLoad.AppHost/Features/ClusterReplication/` owns Docker resource composition; recovery and integration tests mirror `Features/ClusterReplication/`. Node storage adapter remains StorageRecovery. Frontend: N/A, this protocol has no UI. Public client shapes: existing ClientApi contracts; user-visible guarantees remain stable.

Traceability: AC-REP-001/003/004 map to `ClusterTests` and the new replica recovery tests; AC-REP-002 maps to real CrashHost interruption scenarios; AC-REP-005 maps to authorization/failover client cases. TASK-REP-LOG, TASK-REP-TRANSPORT, TASK-REP-INTEGRATE and TASK-REP-VERIFY are defined in [execution plan](../implementation/orleans-foundation.plan.md). Qualification is GitHub Actions only. Historical CI 36926803549 qualifies the old implementation, not this replacement. Power-loss and endurance remain pending.

## Actors, entry points and failure boundaries

Actors are authenticated SDK/MCP callers, the node-local replica host, fixed voters, the membership provider and the recovery operator. Current source is composed by [ServerApplication](../../src/KeyLoad.Server/Features/ClientApi/ServerApplication.cs): it starts the node-local [PartitionHost](../../src/KeyLoad.Server/Features/StorageRecovery/PartitionHost.cs) and Orleans silo, whose [replica service](../../src/KeyLoad.Orleans/Features/ClusterReplication/PartitionReplicaGrainService.cs) routes peer operations. Aspire declares three Docker nodes with separate data mounts. This source is not yet qualified as a delivered RF3 deployment: current tests do not force request-activation migration and then verify storage ownership and a durable caller-visible outcome. Public HTTP/.NET transport belongs to ClientApi; required official MCP parity is pending. Frontend is N/A because replica consensus has no independent UI. Shared contracts stay in Abstractions and the exact ClusterReplication/StorageRecovery slice owners above.

Positive flow: an authorized command reaches its own request grain, the physical host orders and persists it, a majority crosses the declared acknowledgement barrier, and canonical apply returns the stable outcome. Negative flow: minority, stale term/owner, invalid peer MAC/replay or denied principal cannot establish committed success. Edge/error flow: an unknown response is resolved by stable command ID; interrupted/corrupt append or snapshot reopens one verified cut or fails explicitly; cancellation drains owned work without transferring locks to a migrating activation.

AC-REP-006 additionally maps to existing cryptographic/replay source cases in the [ClusterReplication unit slice](../../tests/KeyLoad.UnitTests/Features/ClusterReplication/) and planned real RF3 saturation/failover cases. Pure envelope tests do not prove liveness under load. Every acknowledgement/recovery assertion needs the exact delivered GitHub run; fault/endurance/power-loss claims remain separate.

Related invariants: [ADR-003](../ADR/ADR-003-durability-ack-barrier.md) ACK/profile barriers, [ADR-007](../ADR/ADR-007-replica-consensus-bootstrap.md) consensus/bootstrap, [ADR-016](../ADR/ADR-016-atomic-physical-placement.md) atomic identity/placement and Proposed [ADR-017](../ADR/ADR-017-migration-tokens.md) movement lineage. Proposed token translation does not block the existing fixed RF3 scope; dependent physical movement must wait for its contract.

TASK-REP-DISCOVERY removes the obsolete HTTP consensus/body-spooling protocol.
PeerSecurity authenticates only a bodyless GET of /internal/silo with no query;
native signed envelopes own votes, append payloads and snapshots. The discovery
signature binds a versioned purpose, exact method, recipient authority, path,
timestamp and nonce. A fixed-capacity nonce table rejects saturation explicitly,
retains a nonce through its inclusive future validity, and does not admit malformed
or forged requests. The clock is injected TimeProvider.System in production.
Real request-object security cases cover tamper/replay/expiry/body/path/method and
capacity, while RF3 CI exercises the genuine sockets and signed discovery reply.
No HTTP handler fake or legacy body fallback remains.
