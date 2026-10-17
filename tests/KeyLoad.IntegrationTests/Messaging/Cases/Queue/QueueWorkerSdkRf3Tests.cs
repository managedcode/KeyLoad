namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueWorkerSdkRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualExternalWorkerRefillsWhileSiblingHeldAndJoinsCancellationWithoutAck(bool cancel)
        => QueueWorkerRf3Trial.RunAsync(cancel);
    [Test]
    public Task ActualFreshPersistedRenewalRevocationJoinsHandlerWithoutAckThenColdHealthyWorker()
        => QueueWorkerRf3Trial.RenewalAsync();
    [Test]
    public Task ActualSingleRenewalBudgetStopsAndJoinsHeldHandlerWithoutAckThenColdFreshWorker()
        => QueueWorkerRf3Trial.RenewalBudgetAsync();
    [Test]
    public Task ActualNativeProjectedUtf8ExcessRetainsLeaseWithoutHandlerOrAckThenColdCorrectBudgetWorker()
        => QueueWorkerRf3Trial.ByteRefusalAsync();
    [Test]
    public Task ActualCallerHandlerFailureJoinsWithoutAckThenColdFreshWorker()
        => QueueWorkerRf3Trial.HandlerFailureAsync();
}
