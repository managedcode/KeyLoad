namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class ReplicaTransportShutdownWholeFlowTests(RequestCqrsCohortRuntimeFixture runtime)
{
    [Test]
    public async Task OriginalDiscoveryAndEndpointFailuresJoinBeforeAggregateThenFreshNativeOwnersAreHealthy()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.ShutdownBound, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        await ReplicaTransportShutdownFlow.RunAsync(runtime, caller.Token);
    }
}
