namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed class BlobRetiredPartProcessRecoveryTests
{
    [Test]
    [Arguments(BlobRetiredPartFaultKind.MissingRaw)]
    [Arguments(BlobRetiredPartFaultKind.MissingManifest)]
    [Arguments(BlobRetiredPartFaultKind.WrongManifestHash)]
    public Task PriorAcknowledgedDeletionSurvivesIndependentQueueCutThenPartRepairReclaimAndReuse(BlobRetiredPartFaultKind fault)
        => BlobRetiredPartProcess.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
