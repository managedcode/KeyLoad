namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class BackupJournalRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task HashConsistentJournalRefusalRepairsExactArchiveAndSurvivesTwoColdReopens(bool checkpoint)
        => BackupJournalRepairTrial.RunAsync(checkpoint, TestContext.Current!.Execution.CancellationToken);
}
