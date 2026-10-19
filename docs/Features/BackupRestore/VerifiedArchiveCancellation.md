# Verified archive pre-read cancellation and same-archive continuation

REQ-BACKUP-PRE-READ-CANCEL-001 / AC-BACKUP-PRE-READ-CANCEL-001 refine the original [BackupRestore](../BackupRestore.md) REQ/AC-BACKUP-002 error and owner cleanup contract. A real BackupCutFixture first creates and independently verifies its current-format compacted checkpoint plus retained tail. The actual existing ZoneTreeBackupRestoreRestore.Restore owner receives a genuinely canceled CancellationTokenSource token. This owner validates destination policy and creates its owned staging directory before its first token check; cancellation must preserve the original OperationCanceledException and exact token, remove that stage, preserve the absent or originally empty target, every bounded archive byte and the complete original source identity/position/canonical image. Comparing the complete fixture parent entry set rejects any extra staging residue, rather than accepting only target absence.

REQ-BACKUP-PRE-READ-CANCEL-002 / AC-BACKUP-PRE-READ-CANCEL-002 require the SAME immutable archive to continue through the real CLI restore process after the failed owner has settled. process exit/stdout/stderr/disposal joins and actual CLI receipt are mandatory. The complete independent literal target is the original value row plus DispatchPaused=true, with new physical node/incarnation/signing key, original durability and original source physical cut plus the genuine restore authority commit. Two further target opens each join disposal and preserve the entire target identity/key bytes, canonical rows and physical cut. Original source and all archive bytes remain exact.

```mermaid
flowchart LR
    A[Original verified current-format archive] --> B[Real canceled token at Restore owner]
    B --> C[Original OCE and no retained stage or target effects]
    C --> D[Same archive actual CLI restore]
    D --> E[Literal complete rows and paused fresh identity with CLI receipt]
    E --> F[Two joined cold opens and immutable originals]
```

Tests: `VerifiedArchiveCancellationTests` and `VerifiedArchiveCancellationTrial`. The case CanceledOriginalRestoreBeforeArchiveReadLeavesNoStageThenSameArchiveCliRestoreSurvivesTwoColdOwners has literal Arguments(false/true), using modern TestContext.Current.Execution.CancellationToken. Existing archive/cut/CLI assertions retain their original bounds. Source roles outside this slice are N/A because no product, SDK, MCP, topology, serializer, provider or public cancellation API changes are made. The ordinary public Restore wrapper has no cancellation argument; the negative directly tests its existing internal owner primitive through existing assembly friend access. No synthetic verifier, clock, task or storage is introduced.

This is specifically already-canceled pre-read owner cleanup, not mid-copy cancellation, CLI/process kill or cancellation, stable command resumption, token fencing, replica/domain-command receipt, replicated-cluster restore, power-loss or production qualification. The positive is one CLI restore and two cold reopens. Primary fixture/body and CTS/body failures precede genuine cleanup failures under original ServerFailureObserver lexical lifetimes.
