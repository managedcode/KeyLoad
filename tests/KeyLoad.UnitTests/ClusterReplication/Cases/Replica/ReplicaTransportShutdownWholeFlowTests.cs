namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class ReplicaTransportShutdownWholeFlowTests(RequestCqrsCohortRuntimeFixture runtime)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalDiscoveryAndEndpointFailuresJoinBeforeAggregateThenFreshNativeOwnersAreHealthy(bool frameworkCancelled)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.ShutdownBound, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        await ReplicaTransportShutdownFlow.RunAsync(runtime, frameworkCancelled, caller.Token);
    }
}
