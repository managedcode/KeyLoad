namespace KeyLoad.ClusterTests.Messaging;

internal sealed class QueueWorkerProcessClusterTests
{
    [Test]
    public Task ActualKilledReadOnlyExternalWorkerPreservesLeaseUntilExplicitParentAckThenColdFreshWorker()
        => QueueWorkerClusterTrial.ProcessAsync();
}
