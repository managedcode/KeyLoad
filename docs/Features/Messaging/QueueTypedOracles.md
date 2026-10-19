# Queue typed oracle correction

REQ-QUEUE-TYPED-001: expected quota counters and processed document revision must use Int64 domain constants, without casting actual results or changing expected values. AC-QUEUE-TYPED-001: all four actual QueueCounters fields compare with independent 0L, and actual DocumentResult.Revision compares with independent 1L in both invalid-action and ready-authority healthy flows. REQ-QUEUE-TYPED-002: preserve the cleanup successor, original operation/refusal/replay/recovery/full-image/lease/healthy oracles and all production code unchanged. AC-QUEUE-TYPED-002: exact preguards bind the cleanup InvalidActionTrial and the original ready-authority Healthy assertion; only two long constants and five assertion arguments per helper change. Complete fixture owning ledger remains byte-preserved otherwise.

The original four invalid enum/jitter and eight ready-row expiry cases remain the automated operation tests.

## Canonical source map

The assertion changes are owned by `tests/KeyLoad.UnitTests/Messaging/Scenarios/QueueLifecycle/States/QueueInvalidDeliveryActionTrial.cs` and `tests/KeyLoad.UnitTests/Messaging/Assertions/QueueReadyAuthorityHealthy.cs`. Their callers remain `Cases/QueueInvalidDeliveryActionTests.cs` and `Cases/QueueReadyAuthorityTests.cs` in the same canonical Messaging test slice. This note changes test oracle types only; production behavior and its qualification gates remain unchanged.

```mermaid
flowchart LR
    A[Original queue operation] --> B[Actual QueueCounters and DocumentResult]
    B --> C[Independent Int64 literals in original assertions]
    C --> D[Original refusal image replay and healthy checks]
    D --> E[Original owned fixture cleanup]
```
