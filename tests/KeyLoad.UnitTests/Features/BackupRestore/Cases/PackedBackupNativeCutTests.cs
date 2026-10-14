namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class PackedBackupNativeCutTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task AuthenticContainerTransfersPreserveNativeCutRefusalThenExactRepairAndTwoCold(bool earlierJournal)
        => PackedBackupNativeCutFlow.RunAsync(earlierJournal, TestContext.Current!.Execution.CancellationToken);
}
