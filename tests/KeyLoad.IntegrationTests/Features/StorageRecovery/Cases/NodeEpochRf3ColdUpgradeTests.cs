namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

[NotInParallel]
internal sealed class NodeEpochRf3ColdUpgradeTests
{
    [Test]
    public async Task AcEpoch010And011UpgradePriorThreeVoterStateBeforeStartingCurrentRf3()
    {
        using var deadline = new CancellationTokenSource(NodeEpochRf3Protocol.ParentDeadline);
        await NodeEpochRf3ColdScenario.RunAsync(deadline.Token).ConfigureAwait(false);
    }
}
