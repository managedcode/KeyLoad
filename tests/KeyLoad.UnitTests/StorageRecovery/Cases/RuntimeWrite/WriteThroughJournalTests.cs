namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class WriteThroughJournalTests
{
    [Test]
    public Task AcJournal001RealWriteThroughFlagsCommitReplayAndTwoColdCuts()
        => WriteThroughJournalFlow.RunAsync();

    [Test]
    public Task AcJournal002SymlinkedJournalRefusesThenOriginalRepairRemainsHealthy()
        => WriteThroughJournalRefusalFlow.RunAsync(JournalEntryKind.Symlink);

    [Test]
    public Task AcJournal002FifoJournalRefusesThenOriginalRepairRemainsHealthy()
        => WriteThroughJournalRefusalFlow.RunAsync(JournalEntryKind.Fifo);

    [Test]
    public Task AcJournal002MissingGuardedJournalNeverCreatesThenOriginalRepairRemainsHealthy()
        => WriteThroughJournalRefusalFlow.RunAsync(JournalEntryKind.Missing);
}
