# Queue dispatch control within Messaging

A queue's dispatch state (paused or running) is changed by one explicit typed compare-and-set on the resource definition. ConfigureResource is policy-only (changed existing metadata without CAS is UnsupportedCapability, and the policy-only CAS keeps Paused immutable). The dispatch state therefore cannot be changed by replacing the complete definition (which would lack the expected schema version), by adding CAS alone, or by dropping Paused from the policy comparer; a dedicated operation changes only Paused.

## Scope and contracts

Actual native inventory confirms existing SetDispatch is global, without resource/version, and ConfigureResource is policy-only. Add one explicit typed queue dispatch-state CAS through current metadata authority and the ordered required-copy acknowledgementnowledged apply. The existing ConfigureResource policy-only path remains policy-only; no structural migration, policy bypass, second store, parallel dispatcher, client-owned role or storage-format fallback is permitted.

REQ-QUEUE-DISPATCH-001 / AC-QUEUE-DISPATCH-001: a currently authorized persisted cluster administrator reads the scoped resource metadata summary through the real public API and submits the explicit dispatch operation with original command identity and expected current SchemaVersion. Ordered canonical apply requires that exact version, changes only Paused plus the next SchemaVersion, and preserves Name, Kind, TransactionDomainId, every field/header policy, queue policy, quota, indexes, vector/relational schema and every other stored property. No caller default can replace the persisted definition. The original successful full typed result identifies the dispatch effect; genuine queue seed/ACK receipts retain their distinct effects. No speculative local counter is used.

REQ-QUEUE-DISPATCH-002 / AC-QUEUE-DISPATCH-002: real missing-resource, wrong-kind, stale-version, version-overflow, invalid-input, unauthorized-subject and changed-content-same-command operations must fail through their owning typed contracts, immediately followed by complete current metadata, message bodies/metadata, counters and original receipt observations. A selected failed operation cannot partially change Paused, version, data, policies, indexes, outbox or partition identity. No generic exception substitutes the actual refusal.

REQ-QUEUE-DISPATCH-003 / AC-QUEUE-DISPATCH-003: each accepted state-changing operation advances exactly one schema version. Exact original command replay returns its original full result with fresh persisted authorization and never restores a prior dispatch state over a later pause/resume. Independent stale original version cannot overwrite the successor. Request identity, metadata incarnation, command fingerprint and authority remain the common native contract; no special test deduplication log is introduced.

REQ-QUEUE-DISPATCH-004 / AC-QUEUE-DISPATCH-004: genuine public SDK and official MCP operations, including the current SQL operation envelope where supported, observe canonical persisted Paused after cluster acknowledgment. The original direct AdvanceQueueDeadline not-due, stale and paused refusals remain complete; paused dispatch retains exact scheduled payload/header/state/version/deadline/index/counters. Current operation authorization and read cuts are reloaded after metadata commit; RAM and Orleans caches are disposable acceleration only.

REQ-QUEUE-DISPATCH-005 / AC-QUEUE-DISPATCH-005: resume retains the initial persisted definition and current authority, and the real selected native Orleans due coordinator autonomously promotes the original due message to Ready before any Receive. Persisted principal revocation still refuses, a strictly higher actual policy-epoch repair enables only the original intended grants, and fresh work proceeds.

REQ-QUEUE-DISPATCH-006 / AC-QUEUE-DISPATCH-006: full metadata, command receipts, original messages and state fences survive two genuine same-volume all-three cold cycles owned by the existing Aspire fixture. All original callers/tasks settle before resource stops, fresh real SDK/MCP sessions reconnect after actual readiness, and each initiating/cleanup failure remains retained. Native storage reopen/process recovery is distinct from power-loss durability.

## Canonical source and test ownership

Messaging owns the dispatch-state request/effect contracts and the domain validation/apply helpers; the cross-feature OperationKind, authorization, metadata commit, SDK/MCP/schema inventories and registration are integrated once. Authorization ResourcePolicyUpdates, its structural comparator and the policy-update rejection flows are unchanged. Tests belong in Messaging Cases/Helpers/Assertions/Models and exercise real ZoneTree and the public SDK/MCP clients on the Aspire replicated cluster; the QueueDeadlineClusterPause helper uses this operation with a fresh metadata summary and the current version instead of a default replacement. Frontend is N/A because the SDK/MCP operation is the public boundary. SQL syntax is N/A here: the shared parser and operation envelope own it.

Existing ADR-028/093/094/117/125 retain atomic queue state, policy-only replacement, due execution, native TUnit/Aspire lifecycle and connection-owned signed operation authority.

```mermaid
flowchart LR
    Read[Fresh scoped queue metadata version] --> Authority[Current server authenticated metadata authority]
    Authority --> CAS[Expected schema version]
    CAS --> Dispatch[Only pause state and next version]
    Dispatch --> Commit[Existing ordered cluster metadata commit and receipt]
    Commit --> Paused[Original due attempt refuses and full state remains]
    Paused --> Resume[Explicit current version resume]
    Resume --> Ready[Native service Ready before Receive]
    Ready --> Cold[Joined callers and two owned cold cycles]
```

## Contract and mappings

Frozen implementation: OperationKind.SetQueueDispatch=50 (append-only); QueueDispatchControlRequest generated alias keyload.messaging.queue-dispatch-control-request.v1, required JSON fields and Orleans IDs0..4 TenantId/DatabaseId/Queue/ExpectedSchemaVersion/Paused; positive expected version; /v1/admin/queues/dispatch; keyload_queue_dispatch_control; ResourceDefinition result. Common current cluster administrator and global metadata outcome scope match ConfigureResource. Official MCP effect hints readOnly=false, idempotent=true, destructive=true. Full stored-definition preservation is directly exercised by real ZoneTree unit bytes and independently constructed complete public command result; AdminResources exposes a summary and does not prove hidden policies by itself. Both policy-only pause rejection and global dispatch behavior stay distinct.

Automated mappings: AC001–003 QueueDispatchCasWholeFlowTests; AC002 required HTTP JSON-field presence and native overflow/refusal cases; AC004/006 QueueDispatchCasClusterTests SDK/official HeaderCommand typed results, full existing4-route queue observations/counters/items, old accepted/failed replay before/after exactly two Aspire cluster cold cuts and healthyReceive/Ack. AC005 original QueueDeadlineAutonomousClusterTests keeps its original due service flow and invokes the repaired QueueDeadlineClusterPause. Real process recovery remains the required separately executed recovery gate, never inferred from unit store reopens or cluster cold tests.

The dispatch command returns the complete ResourceDefinition using the existing metadata result contract; it does not introduce or promise a public dispatch CommitReceipt. Original queue seed/ACK CommitReceipt checks remain distinct from dispatch results. No public dispatch cut or private counter is fabricated.
