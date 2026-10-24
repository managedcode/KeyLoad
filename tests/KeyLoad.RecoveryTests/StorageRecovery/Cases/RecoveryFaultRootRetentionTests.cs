namespace KeyLoad.RecoveryTests.StorageRecovery;

[NotInParallel]
internal sealed class RecoveryFaultRootRetentionTests
{
    [Test]
    public async Task ActualFailedAppendRetainsDamagedRootThenExactUndoHealthyReplayAndCold()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await RecoveryFaultRootRetentionFlow.RunAsync(token);
    }
}
