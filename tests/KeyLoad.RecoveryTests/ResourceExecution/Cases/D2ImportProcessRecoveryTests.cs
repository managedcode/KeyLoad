using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

/// <summary>Original local journal ACK interruption during real overlapping imports; not RF3 or power loss.</summary>
[NotInParallel]
internal sealed class D2ImportProcessRecoveryTests
{
    [Test]
    [Arguments(D2InventoryCrashCut.EntryAcknowledged)]
    [Arguments(D2InventoryCrashCut.CommitAcknowledged)]
    [Arguments(D2InventoryCrashCut.AppliedAcknowledged)]
    public Task NativeJournalInterruptionDuringTwoActiveImageImportsPreservesConcurrentEnrollmentThroughCompletionAndTwoColdOwners(
        D2InventoryCrashCut cut)
        => D2ImportRecoveryTrial.RunAsync(cut, TestContext.Current!.Execution.CancellationToken);
}
