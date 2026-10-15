namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueWorkerProcessRf3Tests
{
    [Test]
    public Task ActualKilledReadOnlyExternalWorkerPreservesLeaseUntilExplicitParentAckThenColdFreshWorker()
        => QueueWorkerRf3Trial.ProcessAsync();
}
