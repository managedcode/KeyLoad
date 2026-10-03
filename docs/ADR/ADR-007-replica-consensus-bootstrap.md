# ADR-007: Orleans replica consensus and metadata bootstrap

Status: Accepted; replacement implementation and GitHub qualification pending. Foundation decision: [ADR-036-orleans-foundation](ADR-036-orleans-foundation.md). Related source ownership: [ClusterReplication](../Features/ClusterReplication.md) and [ClusterRouting](../Features/ClusterRouting.md).

## Context and decision

The database cluster uses Orleans as its sole distributed runtime and membership/directory foundation. Consensus, terms/votes/log state, ordered appends, read barriers, snapshot transfer, and replicated metadata are KeyLoad-owned protocols hosted through Orleans. Every request uses its own routing grain; durable storage, journals, locks, and apply gate remain owned by the node-local `PartitionHost` if an Orleans activation migrates. Do not introduce DotNext.AspNetCore.Cluster or DotNext.Net.Cluster.

Bootstrap must establish a fixed RF3 voter set and persisted cluster metadata without a second competing membership authority. Empty/restarted replicas join through verified metadata/snapshot and ordered tail. The precise consensus proof and deployment source of voter identity remain governed by the owning replication contract; no single-node substitute is allowed.

## Rationale and consequences

One cluster foundation avoids split-brain membership and duplicate routing layers. Orleans activation placement is not storage ownership. This requires explicit request routing, stable replica identity, control-plane capacity, quorum barriers, and recovery evidence. Source presence and historical CI do not qualify the replacement implementation.

## Related requirements

`REQ-REP-001..006`/`AC-REP-001..006` and `REQ-ROUTE-001..003`/`AC-ROUTE-001..003`. Related data guarantees: `REQ-MSG-001`/`AC-MSG-001` and `REQ-DSTORE-004`/`AC-DSTORE-004`.

## Implementation contract

1. Freeze voter identity/bootstrap, term/log/cut, read barrier, forwarding, membership changes, and recovery contracts with ADR-036; use Orleans only.
2. Add real TUnit protocol/state tests and process recovery for interrupted vote/append/snapshot; add Aspire Docker RF3 SDK and MCP tests for quorum, minority denial, failover, and migration.
3. Target owners are `src/KeyLoad.Orleans/Features/ClusterReplication/` for Orleans transport, `src/KeyLoad.Orleans/Features/ClusterRouting/` for routing, `src/KeyLoad.Replication/Features/ClusterReplication/` for consensus, and `src/KeyLoad.Server/Features/StorageRecovery/` for node-local `PartitionHost` lifecycle. Shared membership/contracts have one root integration owner.
4. Bootstrap and metadata migration must verify a complete fixed voter identity before serving. Rollout must not advertise readiness before quorum/read barrier; rollback fences the newer ownership epoch and restores only a complete cut.
5. GitHub CI qualifies TUnit, Recovery, and Docker/Aspire RF3 through .NET and official MCP clients. Root reviews every membership/source change and joins artifacts. No local runtime claim.

Dependencies: [ADR-001](ADR-001-partition-identity-affinity.md), [ADR-003](ADR-003-durability-ack-barrier.md), [ADR-004](ADR-004-committed-read-views.md), [ADR-008](ADR-008-backup-log-retention.md), [ADR-011](ADR-011-format-upgrades.md), [ADR-016](ADR-016-atomic-physical-placement.md), and [ADR-036](ADR-036-orleans-foundation.md). Stop on any request to add DotNext or transfer storage ownership to migratable grains; those conflict with root policy.

```mermaid
flowchart LR
    Client[SDK or MCP request] --> RequestGrain[Distinct Orleans request grain]
    RequestGrain --> Replica[Orleans-hosted RF3 consensus protocol]
    Replica --> PartitionHost[Node-local PartitionHost owns storage and apply gate]
    Replica --> Barrier[Quorum commit and read barrier]
    Barrier --> Response[Authorized committed response]
    Bootstrap[Verified voter metadata bootstrap] --> Replica
```

## Owner-directed benchmark fixed membership, 2026-10-03

The production odd-voterRF3-first contract remains mandatory. Explicit trusted
benchmark startup may use fixed native1/2/3 voters under [ADR-056](ADR-056-isolated-linux-comparison-cells.md),
REQ-BC-053/AC-ISO-004 and TASK-ISO-005/010/012. Majority remainsfloor(n/2)+1;
RF1 tolerates no voter loss,RF2 needs both voters for quorum read/write. No HTTP
caller opt-in, alternate storage, membership migration, lowered quorum or production
readiness claim. Exact files/stages/migration/rollback/SDK+MCP fault tests and
root-owned integration are the ADR056 implementation contract. Status staysAccepted
until real benchmark topology and unchangedRF3 fault/recovery qualification exist.
