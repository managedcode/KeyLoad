using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

/// <summary>Genuine process-kill recovery of conservative inventory; not CLUSTER or power-loss qualification.</summary>
[NotInParallel]
internal sealed class D2InventoryProcessRecoveryTests
{
    [Test]
    [Arguments(D2InventoryCrashCut.EntryAcknowledged)]
    [Arguments(D2InventoryCrashCut.CommitAcknowledged)]
    [Arguments(D2InventoryCrashCut.AppliedAcknowledged)]
    public Task NativeJournalCutsRecoverConservativeMembershipWithExactReplayAndTwoColdOwners(D2InventoryCrashCut cut)
        => D2InventoryProcessTrial.RunAsync(cut, D2InventoryRecoveryFlow.VerifyAsync,
            TestContext.Current!.Execution.CancellationToken);
}
