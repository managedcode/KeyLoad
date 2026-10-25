
namespace KeyLoad.ClusterTests.Messaging;

internal sealed class RemoteTransferThreeStageColdTests
{
    [Test]
    public Task OriginalPendingTargetReceiptAndDeliveredSurviveThreeColdCutsWithFourRouteRefusalReplayAndHealthyTransfer()
        => RemoteTransferColdTrial.RunAsync();
}
