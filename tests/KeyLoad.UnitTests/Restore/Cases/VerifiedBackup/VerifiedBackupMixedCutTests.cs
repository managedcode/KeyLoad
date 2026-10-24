namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class VerifiedBackupMixedCutTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task AuthenticOriginalArchiveJournalFromAnotherAcknowledgedCutRefusesThenSameTupleRepairAndTwoCold(bool earlierJournal)
        => VerifiedBackupMixedCutFlow.RunAsync(earlierJournal, TestContext.Current!.Execution.CancellationToken);
}
