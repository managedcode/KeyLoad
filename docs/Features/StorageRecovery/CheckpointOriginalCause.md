# Original checkpoint failure cause

REQ-STORAGE-008 / AC-SQ-008 requires a failed native generation replacement to close admission until recovery while retaining the original operation failure. Existing `ZoneTreeCheckpointGeneration.Replace` poisoned the runtime and returned safe `RecoveryRequired`, but discarded its original exception. Preserve that exact code, safe detail, poison boundary and every original write/flush/rename/apply order; use the existing `Errors.Fail(code, detail, cause)` overload. No exception object is added to public SDK/MCP replies.

The two native TUnit arguments perform real Compact and InstallSnapshot operations against actual ZoneTree files. A directory at the original identity temporary-file path makes the existing BCL durable writer fail. Require the original filesystem cause beneath the existing typed failure, byte-exact original identity and journal, unchanged position, and refused reads/writes on the poisoned live owner. Dispose that owner, remove only the owned obstruction, reopen and verify the complete two-record image and full identity; the same actual checkpoint operation then succeeds and two cold opens retain the complete expected state. All original body/disposal failures are observed separately.

```mermaid
flowchart LR
 CommittedRows --> TemporaryDirectory --> ActualCheckpointFailure
 ActualCheckpointFailure --> OriginalCauseAndPoison --> DisposeAndRepair
 DisposeAndRepair --> ReopenOriginalRows --> HealthyCheckpoint --> TwoColdOpens
```

This is not process-crash, replicated-cluster or power-loss evidence; recovery and fault qualification remain mandatory. A failure during outer temporary-file deletion could still mask an earlier error and requires a separate genuine producer; this narrow repair does not claim to fix that path or ambiguous filesystem rename failures. See [StorageRecovery](../StorageRecovery.md).
