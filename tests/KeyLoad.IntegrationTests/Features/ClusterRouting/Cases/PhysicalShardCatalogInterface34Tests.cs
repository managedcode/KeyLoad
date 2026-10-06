namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PhysicalShardCatalogInterface34Tests
{
    [Test]
    public async Task Interface3ColdUpgradeMixedFenceAndRestoreOnlyRollback()
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        await PhysicalShardCatalogInterface34Scenario.RunAsync(deadline.Token).ConfigureAwait(false);
    }
}
