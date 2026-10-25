namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class RemoteTransferLateTargetBatchColdTests
{
    [Test]
    public Task LaterTargetBatchConflictRollsBackReceiptMessageAndCapacityThenFreshRepairCompletesAcrossTwoColdCuts()
        => RemoteTransferLateTargetBatchColdTrial.RunAsync();
}
