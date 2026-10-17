namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class BackupCreateSourceAccessTests
{
    [Test]
    public Task OriginalSourceJournalAccessFailureLeavesUnpublishedOutputThenSamePathCaptureCliRestoreAndTwoCold()
        => BackupCreateSourceAccessFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
