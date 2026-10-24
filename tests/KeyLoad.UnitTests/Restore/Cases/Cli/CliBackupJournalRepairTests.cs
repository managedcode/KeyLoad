namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class CliBackupJournalRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task ActualCliRejectsFrameChecksumThenRepairsOriginalAndPreservesTwoColdCuts(bool checkpoint)
        => CliBackupJournalRepairTrial.RunAsync(checkpoint, TestContext.Current!.Execution.CancellationToken);
}
