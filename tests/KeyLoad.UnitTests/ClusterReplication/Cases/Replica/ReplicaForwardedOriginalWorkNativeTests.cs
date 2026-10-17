namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaForwardedOriginalWorkNativeTests
{
    [Test]
    public Task AuthenticatedForwardOwnsOneBudgetBeforeAdmissionAndCancelledQueueJoinsBeforeLedgerReleaseThenHealthyReplay()
        => ReplicaForwardedOriginalWorkFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
