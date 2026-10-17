
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferThreeStageColdTests
{
    [Test]
    public Task OriginalPendingTargetReceiptAndDeliveredSurviveThreeColdCutsWithFourRouteRefusalReplayAndHealthyTransfer()
        => RemoteTransferColdTrial.RunAsync();
}
