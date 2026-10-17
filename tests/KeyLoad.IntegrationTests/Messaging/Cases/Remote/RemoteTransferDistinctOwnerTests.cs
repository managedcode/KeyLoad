namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferDistinctOwnerTests
{
    [Test]
    public async Task ActualDistinctOwnersRetainAcceptReceiptAcrossTwoColdRestartsAndBDoesNotResurrectAcknowledgedMessage()
        => await RemoteTransferDistinctTrial.RunAsync();
}
