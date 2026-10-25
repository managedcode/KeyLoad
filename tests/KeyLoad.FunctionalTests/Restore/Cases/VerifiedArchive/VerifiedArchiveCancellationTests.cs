namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class VerifiedArchiveCancellationTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task CanceledOriginalRestoreBeforeArchiveReadLeavesNoStageThenSameArchiveCliRestoreSurvivesTwoColdOwners(bool existingEmpty)
        => VerifiedArchiveCancellationTrial.RunAsync(existingEmpty, TestContext.Current!.Execution.CancellationToken);
}
