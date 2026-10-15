namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

[NotInParallel]
internal sealed class RecoveryFaultRootRetentionTests
{
    [Test]
    public async Task ActualFailedNativeAppendRetainsDamagedRootThenExactUndoHealthyReplayAndCold()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await RecoveryFaultRootRetentionFlow.RunAsync(token);
    }
}
