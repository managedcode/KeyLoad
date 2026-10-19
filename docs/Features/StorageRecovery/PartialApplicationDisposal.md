# Generic application disposal after partial startup

This case covers [ADR-136](../../ADR/ADR-136-orleans-partition-ownership.md) and the ownership contract in [StorageRecovery](../StorageRecovery.md). Connection ownership follows [ADR-125](../../ADR/ADR-125-connection-owned-execution.md), and TUnit ownership follows [ADR-117](../../ADR/ADR-117-tunit-ci-entry.md).

## Operation and independent oracles

The case calls production `ServerConfiguration.BuildAsync` with the shared `RequestCqrsHostedProfile` and localhost membership. One silo and one persisted copy are configured. The original arguments, identities, physical directory and port are frozen for all applications. The ApplicationServices participant performs a real second `node.owner.lock` acquisition after successful storage Init. The original startup task must fail with that exact IOException. Complete bounded canonical records, full storage identity and physical position are compared before and after the refusal under the same storage read gate. Unexpected startup errors remain before proof errors.

Before invalidating the provider, the test captures the actual physical owner, node and connection registry. The canonical store's `identity.json` and `commands.wal` are read with one aggregate byte bound from the actual database profile. FileStream reads allocate only their bounded declared lengths, reject an additional byte, retain the caller token, and preserve read and independent reader-disposal failures. Private bytes are compared as booleans and never emitted in assertions. Derived tree files are not an invariant.

The only shutdown operation initiating cleanup is the original `WebApplication.DisposeAsync`. No application Stop, node Stop, registry shutdown or storage-lifecycle call precedes it. Actual owner Closing and node Closing must succeed, request work and the complete node lifetime must be joined, and `CanReleaseRoot` must be true. The test does not invoke registry methods to repair missing cleanup. After successful disposal and release permission, it reacquires the existing OS node lock and disposes that handle independently. Both canonical authority files must remain byte-identical.

The same original arguments and root then enter the shared healthy-operation helper. ConfigureResource and Batch commands, a connection-grain CQRS minimum read and exact replay must preserve the independent complete `ProcessDurable` receipt, one persisted copy, empty private CompositionReferences, whole literal document and raw persisted StoredOutcome bytes. Replay cannot advance the canonical or physical cut. Two actual cold applications repeat full storage identity, receipt, outcome, minimum-read and release assertions. Original body, file-reader, direct-disposal and assertion errors remain observable. Any failure retains the directory and attaches its bounded root path to the original failure; a faulted cleanup task never implies release.

```mermaid
sequenceDiagram
    participant App as Same production application
    participant GS as storage Init
    participant Trial as Test observation at ApplicationServices
    participant Owner as Actual terminal physical owner
    participant Fresh as Same-root applications
    App->>GS: startup
    GS->>Owner: Original opening
    Trial->>Owner: Real held-lock acquisition refusal
    App-->>Trial: Original startup failure
    Trial->>App: ONLY original DisposeAsync
    App->>Owner: Generic DI terminal disposal
    Owner->>Owner: Registry, requests, storage lifetime
    Owner-->>Trial: Original joins and actual release permit
    Trial->>Trial: OS lock reacquisition and file conservation
    Trial->>Fresh: healthy operation, replay and two cold applications
```

## Qualification boundary

Existing mandatory case: `PartitionStorageApplicationDisposeTests.ActualGenericApplicationDisposeAfterPartialStartupRetainsOwnersThenCanonicalReceiptThroughTwoColdStarts`.

The fault occurs during ApplicationServices before Active. The case does not prove shutdown of active background stateless-worker work; the distinct `PartitionStorageProducerDispose*` family owns that gate. silo termination is also separate from the physical-owner release permit. The explicit partial-start Stop case in [PartitionStorageStartup](PartitionStorageStartup.md) owns the exact Starting-state Stop-error oracle. This case observes only the original generic disposal and its real terminal outcomes.

This source adaptation has not yet been compiled or run. It describes single-silo production composition and physical-file checks. Aspire cluster operations, public SDK/official MCP interoperability, abrupt process recovery, power loss, Linux runtime, OS behavior, deadline fit and broader recovery/endurance remain independent open gates. The configured startup timing, original cancellation, resource limits and joined cleanup remain required.
