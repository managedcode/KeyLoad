namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class SeededWalCorruptionWholeProcessTests
{
    [Test]
    [Arguments(1701)]
    [Arguments(1702)]
    [Arguments(1703)]
    public async Task SavedSeedCorruptsOnlyCompleteActualNativeFrameThenTypedFailStopExactRepairHealthyTriadReplayAndCold(int seed)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await SeededWalCorruptionFlow.RunAsync(seed, token);
    }
}
