namespace KeyLoad.RecoveryTests.StorageRecovery;

internal sealed class SeededWalCorruptionWholeProcessTests
{
    [Test]
    [Arguments(1701)]
    [Arguments(1702)]
    [Arguments(1703)]
    public async Task SavedSeedCorruptsOnlyCompleteActualFrameThenTypedFailStopExactRepairHealthyTriadReplayAndCold(int seed)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var admission = await StorageTrialLease.AcquireAsync(token);
        await SeededWalCorruptionFlow.RunAsync(seed, token);
    }
}
