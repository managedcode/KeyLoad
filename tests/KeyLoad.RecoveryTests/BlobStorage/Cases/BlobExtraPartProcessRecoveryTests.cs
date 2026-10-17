namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed class BlobExtraPartProcessRecoveryTests
{
    [Test]
    [Arguments(BlobExtraPartFault.ExtraRaw)]
    [Arguments(BlobExtraPartFault.ExtraManifest)]
    public Task PriorAcknowledgedTwoPartDeletionSurvivesIndependentQueueCutThenExtraPartRepairReclaimAndReuse(BlobExtraPartFault fault)
        => BlobExtraPartProcess.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
