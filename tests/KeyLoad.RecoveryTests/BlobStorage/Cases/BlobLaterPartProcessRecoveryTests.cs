namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed class BlobLaterPartProcessRecoveryTests
{
    [Test]
    [Arguments(BlobLaterPartFault.MissingSecondRaw)]
    [Arguments(BlobLaterPartFault.AlteredSecondRaw)]
    public Task PriorAcknowledgedTwoPartDeletionSurvivesIndependentQueueCutThenLaterPartRepairReclaimAndReuse(BlobLaterPartFault fault)
        => BlobLaterPartProcess.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
