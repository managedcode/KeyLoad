namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueInvalidDeliveryActionTests
{
    [Test]
    [Arguments(QueueRetryJitter.None, -1)]
    [Arguments(QueueRetryJitter.None, 99)]
    [Arguments(QueueRetryJitter.Full, -1)]
    [Arguments(QueueRetryJitter.Full, 99)]
    public Task UnsupportedNativeActionPreservesCurrentLeaseThenProcessingCommitsExactlyOnce(
        QueueRetryJitter jitter, int action)
        => QueueInvalidDeliveryActionTrial.RunAsync(jitter, action, TestContext.Current!.Execution.CancellationToken);
}
