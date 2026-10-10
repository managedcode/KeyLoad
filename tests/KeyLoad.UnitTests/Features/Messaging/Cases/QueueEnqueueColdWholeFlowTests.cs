namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueEnqueueColdWholeFlowTests
{
    [Test]
    public Task OriginalEnqueueImageAndReceiptSurviveColdReplayRefusalsThenAuthorizedHealthyContinuation()
        => QueueEnqueueColdTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
