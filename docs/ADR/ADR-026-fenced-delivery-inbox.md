# ADR-026: At-least-once delivery with fenced leases and inbox effects

Status: Accepted; end-to-end replicated-cluster delivery and reconciliation qualification pending.

## Context and decision

Durable queue delivery can repeat after lost responses, worker restarts, or expired leases. Exactly-once handler execution cannot be guaranteed for arbitrary user code or external services. The database can provide at-least-once delivery and protect canonical effects by fencing stale claims and atomically committing inbox identity, database effects, and local acknowledgement.

Every claim has a persisted lease version/generation and signed token bound to message, lane, principal, incarnation, and expiry. ACK, NACK, and renew validate current ownership. `CommitProcessing` deduplicates handler input/fingerprint and commits permitted same-partition effects plus inbox receipt and ACK atomically. External calls remain outside the transaction and require their own idempotency protocol.

```mermaid
sequenceDiagram
    participant Queue
    participant Worker
    participant DB as Atomic database partition
    Queue-->>Worker: At-least-once delivery with fenced lease
    Worker->>DB: CommitProcessing(input identity, effects)
    DB->>DB: Validate lease, permissions, inbox, and effects
    DB-->>Worker: Durable receipt and ACK outcome
    Worker->>Queue: Retry only with the defined receipt/lease protocol
```

## Alternatives and consequences

Claiming exactly-once external execution is rejected. Unfenced ACK can let an expired worker acknowledge a reclaimed message. Separate effect and ACK commits create duplicate effects after a lost response. Atomic inbox state protects database effects but does not make arbitrary handler code or network side effects transactional.

## Related requirements and implementation contract

Related: `REQ-MSG-001..003/AC-MSG-001..003`, ADR-023/024/027/029/030.

1. Freeze lease-token fields, dedup identity/horizon, supported atomic scope, and external-side-effect caveat.
2. Test lost claim/ACK response, stale and expired worker, changed fingerprint, permission revocation, quota failure, process kill, and reopen with real store/process.
3. Implement lease state and inbox effect transaction in `src/KeyLoad.Core/Messaging/`; keep worker transport in the ClientApi/Messaging boundary and Orleans routing in its existing ClusterRouting slice.
4. Fence stale token generations. Rollback may restore only a verified cut matching retained inbox receipts and canonical effects; assign a new/fenced identity, keep delivery paused, and reconcile before redelivery. Never reactivate stale lease tokens or repeat a known canonical effect. The current wire and persisted-data formats remain fixed.
5. Run GitHub TUnit, process recovery, and replicated-cluster SDK/MCP races; publish only exact-SHA evidence.

Current source: `src/KeyLoad.Core/Messaging/Execution/Messaging.cs`, `src/KeyLoad.Core/DatabaseEngine.cs`, `src/KeyLoad.Abstractions/Contracts.cs`; tests: `tests/KeyLoad.UnitTests/MessagingTests.cs`, `SubscriptionTests.cs`, `TransactionTests.cs`. The feature contract is [Messaging](../Features/Messaging.md). No external exactly-once claim is made.

## Multi-lane receive composition

Accepted implementation contract for REQ-MSG-007 / AC-MSG-007; qualification remains pending. The complete normative
request/result, ordering, bounded admission, per-lane authorization/read-cut,
atomicity, cancellation and uncertainty contract is in
[Messaging](../Features/Messaging.md).

Implement in order: freeze that contract; add native attributed Messaging types
and centrally validated lane ceiling; compose existing claims through the connection grain
and joined native Communication CQRS streams; bind one SDK/HTTP/MCP
operation and its SQL CALL inventory; add real ZoneTree and public replicated-cluster whole flows;
then run every required native gate and retain original results before status
changes. Existing identity and commit lifetimes are reused, no new
scheduler, read-cut authority, persisted group receipt or cross-partition commit.
Explicit request-to-request graph permission is limited to the existing native
ExecuteStreamAsync transition; a multi-lane parent only emits ordinary Receive
children and cannot recursively emit multi-lane children. Field aliases/IDs of
existing persisted claims remain fixed. Rollback drains the new ingress while
preserving committed lease/receipt recovery.

```mermaid
flowchart LR
  API[SDK / MCP / SQL CALL] --> Parent[Connection grain]
  Parent --> Child[One native Receive call at a time]
  Child --> Owner[Silo-local ZoneTree owner]
  Owner --> Outcome[Independent committed / rejected / unknown outcome]
```

## Public renewal/reclaim cold fencing, 2026-10-10
