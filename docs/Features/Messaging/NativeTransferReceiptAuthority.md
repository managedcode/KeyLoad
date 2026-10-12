# Native transfer receipt authorization after a retained child

REQ-XFER-RECEIPT-PRIVACY-001 / AC-XFER-RECEIPT-PRIVACY-001, ADR088/125. The existing post-await whole case already commits a real B child, revokes A QueuePublish, observes UnknownWriteOutcome, repairs the same persisted principal and reconciles the original Accept. QueueInspect was retained there, so it does not prove denied receipt inspection. Add this distinct test-only stage after that original policy repair, before original same-ID replay. Remove only destination QueueInspect from the same persisted logical A principal; leave QueuePublish, Query, identity and all other grants unchanged. The Core native ReadRemoteTransferReceiptDispatch requires current source/destination inspector grants before delegation. SDK, official MCP, SDK Q1 and MCP Q1 receipt reads must fail PermissionDenied with no data; current native six-owner cuts remain byte-exact. Restore that grant by another actual persisted policy epoch advance, read the genuine retained receipt with literal source/destination/transfer/original target commit checks, then execute the unchanged same-ID replay, two cold cuts and healthy/public continuation.

REQ-XFER-RECEIPT-PRIVACY-002 / AC-XFER-RECEIPT-PRIVACY-002: preserve the original method/UID, actual parent request, child receipt, scope/read budget, full cuts, failed outcome/cleanup ledger, deadline/cancellation, original principal and old Create refusal. No new product hook, public DTO, stored field, trusted role, provider or dispatch path. Any failed stage is retained by the original lifetime and retains roots; a test failure cannot synthesize repair success. Complete integrated compiler/native UID and exact-source Linux public runtime gates remain open.

```mermaid
flowchart LR
    Child[Original genuine B commit] --> Repair[Original A policy repair]
    Repair --> Revoke[Remove A destination QueueInspect]
    Revoke --> Deny[Four actual public receipt reads denied]
    Deny --> Cut[Exact six-owner no-effect cut]
    Cut --> Restore[Restore same persisted grant]
    Restore --> Receipt[Genuine retained receipt]
    Receipt --> Original[Original replay two cold and healthy chain]
```
