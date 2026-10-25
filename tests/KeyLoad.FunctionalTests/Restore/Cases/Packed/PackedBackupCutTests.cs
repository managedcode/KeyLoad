namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class PackedBackupCutTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task AuthenticContainerTransfersPreserveCutRefusalThenExactRepairAndTwoCold(bool earlierJournal)
        => PackedBackupCutFlow.RunAsync(earlierJournal, TestContext.Current!.Execution.CancellationToken);
}
