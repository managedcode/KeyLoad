namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PhysicalShardCatalogInterface34Tests
{
    [Test]
    public async Task Interface3ColdUpgradeMixedFenceAndRestoreOnlyRollback()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(RequestCqrsRf3Protocol.ParentDeadline);
        await PhysicalShardCatalogInterface34Scenario.RunAsync(deadline.Token).ConfigureAwait(false);
    }
}
