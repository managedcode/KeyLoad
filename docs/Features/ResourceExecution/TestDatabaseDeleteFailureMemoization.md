# fixture deletion failure memoization

REQ-FIXTURE-DELETE-001 / AC-FIXTURE-DELETE-001 creates a genuine TestDatabase, configures its collection, commits and replays a fixed original command with the complete independently expected receipt, full document/record/page and canonical state from the [concurrent-close contract](TestDatabaseConcurrentSuccessfulClose.md). Only actual successful JoinOwnedResources admits a complete closed-owner filesystem baseline. The finite inventory uses the actual fixture MaxScanRecords entry cap, MaxQueryReadBytes total file-byte cap and storage FileBufferBytes; it includes all files, directories and original Unix modes, including the root. It rejects a linked root before enumeration and rejects every linked entry before enqueueing a directory. The explicit nonrecursive queue shares one total entry cap plus one-entry lookahead across the complete inventory; links are refused rather than skipped. Original owners are closed and this private fixture admits no external path-swap actor; no hostile TOCTOU proof is claimed. It is not publication or a performance measurement.

REQ-FIXTURE-DELETE-002 / AC-FIXTURE-DELETE-002 temporarily removes ALL read and write bits from ONLY that owned root, preserving original execute and other bits. A genuine default Directory.EnumerateFileSystemEntries(root).ToArray must first refuse with UnauthorizedAccessException. If root privileges, capabilities or ACLs permit enumeration, the precondition fails honestly, mode is independently restored and fixture.Dispose is never invoked. There is no skip, fabricated permission exception, changed subtree mode or unrelated filesystem intervention. Windows is explicitly unsupported by this test; Linux qualification is required.

The release-source law is explicit: .NET10.0.10 Directory uses EnumerationOptions.Compatible (IgnoreInaccessible=false), Unix enumeration opens the root directory before its first child, and recursive deletion uses that same enumeration. With the verified real root enumeration denial, deletion refuses before any child is visited. This differs from removing write bits alone while allowing enumeration: that is insufficient because recursive deletion may continue into writable subdirectories after its first error. Release source is not proof of the installed runtime or filesystem behavior; the actual precondition and full inventory oracle are mandatory.

REQ-FIXTURE-DELETE-003 / AC-FIXTURE-DELETE-003 captures the actual first UnauthorizedAccessException from fixture.Dispose. Exact original mode restoration is observed independently even after body failure. Before reopen, the complete original files/bytes/directory/mode inventory must match. Repeated Dispose with repaired permissions must rethrow that SAME exception instance, preserve the root and preserve the full inventory. This qualifies initial deletion failure memoization only; it does not claim store Dispose failure, nested replica constructor cleanup, atomic recursive deletion or power-loss durability.

REQ-FIXTURE-DELETE-004 / AC-FIXTURE-DELETE-004 reacquires the actual canonical and replica locks and strictly reopens the same canonical owner, verifies full original identity/current/canonical logical state and original receipt replay, then joins the reopened owner. Disposable tree files may change during that legitimate reopen; the physical byte comparison applies before reopening, not across recovery. Only zero unexpected errors and proven closed owners admit explicit manual cleanup of the original root. Any precondition, restoration, inventory, reopen or close failure retains the root; no subject deletion retry is used as cleanup. An independent healthy fixture then completes the full acknowledged/replay/join/reopen/repeated-close operation.

The [ResourceExecution](../ResourceExecution.md) feature and [ADR-117](../../ADR/ADR-117-tunit-ci-entry.md) retain TUnit execution. Real compiler, runtime, Recovery/cluster and Linux evidence remain required.

```mermaid
flowchart LR
  Seed[full receipt and replay] --> Join[Original owners joined]
  Join --> Baseline[Complete bounded file and mode inventory]
  Baseline --> Deny[Root read and write denied]
  Deny --> Probe[Actual enumeration must refuse]
  Probe --> Failure[Actual first Dispose failure]
  Probe --> Capability[If enumeration succeeds fail before Dispose]
  Failure --> Restore[Restore exact original mode]
  Capability --> Restore
  Restore --> Repeat[Same exception and full unchanged inventory]
  Repeat --> Reopen[lock and strict full logical reopen]
  Reopen --> Clean[Proven closed manual cleanup]
  Clean --> Healthy[Independent healthy fixture]
```
