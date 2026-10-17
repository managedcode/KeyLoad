namespace KeyLoad.UnitTests.Features.ClusterReplication;

[RequestCqrsCohortDataSource]
[NotInParallel]
internal sealed class ReplicaProtocolFileFailureTests(RequestCqrsCohortRuntimeFixture runtime)
{
    [Test]
    public async Task ActualCallbackFileFailureKeepsOriginalProtocolDisposalPendingUntilForwardSettlesThenFreshNativeOwnerIsHealthy()
    {
        using var timeout = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.ShutdownBound, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        await ReplicaProtocolFileFailureFlow.RunAsync(runtime, caller.Token);
    }
}
