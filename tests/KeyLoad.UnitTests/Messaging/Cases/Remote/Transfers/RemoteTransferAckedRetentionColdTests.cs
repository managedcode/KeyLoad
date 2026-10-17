namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferAckedRetentionColdTests
{
    [Test]
    public Task FullTargetRetentionAfterAckRefusesNewIntentButOriginalReceiptsCompleteAcrossTwoColdCuts()
        => RemoteTransferAckedRetentionColdTrial.RunAsync();
}
