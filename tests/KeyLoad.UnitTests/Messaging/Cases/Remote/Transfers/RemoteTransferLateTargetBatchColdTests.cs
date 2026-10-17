namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RemoteTransferLateTargetBatchColdTests
{
    [Test]
    public Task LaterTargetBatchConflictRollsBackReceiptMessageAndCapacityThenFreshRepairCompletesAcrossTwoColdCuts()
        => RemoteTransferLateTargetBatchColdTrial.RunAsync();
}
