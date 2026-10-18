namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed class BlobDeleteCommitCutProcessRecoveryTests
{
    [Test]
    [Arguments(BlobCommitCutKind.HeaderWritten)]
    [Arguments(BlobCommitCutKind.JournalFlushed)]
    [Arguments(BlobCommitCutKind.UnknownAfterJournalFlush)]
    [Arguments(BlobCommitCutKind.Acknowledged)]
    public Task ActualDeletePublicationCutRecoversOriginalReceiptRetainedPartsReclaimAndHealthyTwoColdOwners(BlobCommitCutKind cut)
        => BlobCommitCutProcess.RunAsync(OperationKind.DeleteBlob, cut, BlobDeleteCommitCutTrial.RunAsync,
            TestContext.Current!.Execution.CancellationToken);
}
