namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class NativeBackupJournalRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task HashConsistentJournalRefusalRepairsExactArchiveAndSurvivesTwoColdReopens(bool checkpoint)
        => NativeBackupJournalRepairTrial.RunAsync(checkpoint, TestContext.Current!.Execution.CancellationToken);
}
