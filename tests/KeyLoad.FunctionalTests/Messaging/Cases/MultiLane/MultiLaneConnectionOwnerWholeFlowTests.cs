namespace KeyLoad.FunctionalTests.Messaging;

/// <summary>Actual connection-local child claims, replay and joined healthy continuation.</summary>
internal sealed class MultiLaneConnectionOwnerWholeFlowTests
{
    [Test]
    public Task ParentAndSignedLeavesShareOwnerReplayExactClaimsThenAckAndClose()
        => MultiLaneConnectionOwnerTrial.RunAsync();
}
