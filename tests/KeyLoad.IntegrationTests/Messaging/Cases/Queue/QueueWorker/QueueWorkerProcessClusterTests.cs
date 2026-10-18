namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueWorkerProcessClusterTests
{
    [Test]
    public Task ActualKilledReadOnlyExternalWorkerPreservesLeaseUntilExplicitParentAckThenColdFreshWorker()
        => QueueWorkerClusterTrial.ProcessAsync();
}
