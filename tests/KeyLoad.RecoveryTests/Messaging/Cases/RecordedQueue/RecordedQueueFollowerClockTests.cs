namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class RecordedQueueFollowerClockTests
{
    [Test]
    public Task OriginalNativeLeaderDeadlineAndRenewalEntriesMaterializeIdenticallyWithOffsetFollowerUtcAndSameRootCold()
        => RecordedQueueFollowerFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
