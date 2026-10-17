using KeyLoad.CrashHost.Features.BlobStorage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed class BlobCompleteCommitCutProcessRecoveryTests
{
    [Test]
    [Arguments(BlobCommitCutKind.HeaderWritten)]
    [Arguments(BlobCommitCutKind.JournalFlushed)]
    [Arguments(BlobCommitCutKind.UnknownAfterJournalFlush)]
    [Arguments(BlobCommitCutKind.Acknowledged)]
    public Task AcBlob004GenuineCompleteCutRetainsPriorAckThenOriginalRetryAndTwoColdOwners(BlobCommitCutKind cut)
        => BlobCommitCutProcess.RunAsync(OperationKind.CompleteBlobUpload, cut,
            BlobCompleteCommitCutTrial.RunAsync, TestContext.Current!.Execution.CancellationToken);
}
