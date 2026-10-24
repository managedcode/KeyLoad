namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class BackupRequiredFileAccessTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task OriginalRequiredArchiveFileAccessRefusesThenSamePathCliRepairAndTwoColdOwners(bool existingEmpty)
        => BackupRequiredFileAccessFlow.RunAsync(existingEmpty, TestContext.Current!.Execution.CancellationToken);
}
