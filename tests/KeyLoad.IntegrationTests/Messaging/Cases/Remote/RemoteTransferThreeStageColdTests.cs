
namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class RemoteTransferThreeStageColdTests
{
    [Test]
    public Task OriginalPendingTargetReceiptAndDeliveredSurviveThreeColdCutsWithFourRouteRefusalReplayAndHealthyTransfer()
        => RemoteTransferColdTrial.RunAsync();
}
