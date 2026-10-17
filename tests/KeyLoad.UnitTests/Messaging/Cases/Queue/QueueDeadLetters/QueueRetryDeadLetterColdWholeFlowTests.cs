namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueRetryDeadLetterColdWholeFlowTests
{
    [Test]
    public Task ActualCappedRetryDeadLetterQuotaRefusalExpiryAndCancellationPreservePendingBodyThenHealthyAckAndColdReplay()
        => QueueRetryColdTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
