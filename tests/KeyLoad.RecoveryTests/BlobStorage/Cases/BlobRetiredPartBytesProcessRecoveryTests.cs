namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed class BlobRetiredPartBytesProcessRecoveryTests
{
    [Test]
    [Arguments(BlobRetiredPartBytesFault.AlteredRawBytes)]
    [Arguments(BlobRetiredPartBytesFault.WrongManifestLength)]
    public Task PriorAcknowledgedDeletionSurvivesIndependentQueueCutThenByteRepairReclaimAndReuse(BlobRetiredPartBytesFault fault)
        => BlobRetiredPartBytesProcess.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
