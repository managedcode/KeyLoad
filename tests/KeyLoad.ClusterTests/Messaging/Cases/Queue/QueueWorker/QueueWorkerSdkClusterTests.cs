namespace KeyLoad.ClusterTests.Messaging;

internal sealed class QueueWorkerSdkClusterTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualExternalWorkerRefillsWhileSiblingHeldAndJoinsCancellationWithoutAck(bool cancel)
        => QueueWorkerClusterTrial.RunAsync(cancel);
    [Test]
    public Task ActualFreshPersistedRenewalRevocationJoinsHandlerWithoutAckThenColdHealthyWorker()
        => QueueWorkerClusterTrial.RenewalAsync();
    [Test]
    public Task ActualSingleRenewalBudgetStopsAndJoinsHeldHandlerWithoutAckThenColdFreshWorker()
        => QueueWorkerClusterTrial.RenewalBudgetAsync();
    [Test]
    public Task ActualCallerHandlerFailureJoinsWithoutAckThenColdFreshWorker()
        => QueueWorkerClusterTrial.HandlerFailureAsync();
}
