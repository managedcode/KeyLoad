namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class QueueRetryRetainedConflictWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task NativeEmptyReceiveRetainsOriginalAcrossChangedContentNoAppendThenFreshClaimAckAndHealthy(bool byteLimit)
        => QueueRetryRetainedConflictFlow.RunAsync(byteLimit);
}
