namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TenantDedicatedOwnerRf3Tests
{
    [Test]
    public async Task ExplicitDedicatedTenantTransferPreservesPackedSiblingAcrossDeniedCallerReplayAndTwoColdCuts()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadline.Token);
        await TenantDedicatedOwnerRf3Scenario.RunAsync(caller.Token);
    }
}
