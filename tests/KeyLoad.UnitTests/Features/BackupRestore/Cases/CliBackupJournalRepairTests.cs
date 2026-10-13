namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupJournalRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task ActualCliRejectsNativeFrameChecksumThenRepairsOriginalAndPreservesTwoColdCuts(bool checkpoint)
        => CliBackupJournalRepairTrial.RunAsync(checkpoint, TestContext.Current!.Execution.CancellationToken);
}
