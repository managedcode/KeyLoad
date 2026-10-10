namespace KeyLoad.UnitTests.Features.Messaging;

/// <summary>Actual connection-local child claims, replay and joined healthy continuation.</summary>
internal sealed class MultiLaneConnectionOwnerWholeFlowTests
{
    [Test]
    public Task ParentAndSignedLeavesShareNativeOwnerReplayExactClaimsThenAckAndClose()
        => MultiLaneConnectionOwnerTrial.RunAsync();
}
