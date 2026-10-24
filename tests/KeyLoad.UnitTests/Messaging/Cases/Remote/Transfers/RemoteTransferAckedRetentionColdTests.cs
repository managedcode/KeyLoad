namespace KeyLoad.UnitTests.Messaging;

internal sealed class RemoteTransferAckedRetentionColdTests
{
    [Test]
    public Task FullTargetRetentionAfterAckRefusesNewIntentButOriginalReceiptsCompleteAcrossTwoColdCuts()
        => RemoteTransferAckedRetentionColdTrial.RunAsync();
}
