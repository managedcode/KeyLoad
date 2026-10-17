namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeWriteThroughJournalTests
{
    [Test]
    public Task AcNativeJournal001RealWriteThroughFlagsCommitReplayAndTwoColdCuts()
        => NativeWriteThroughJournalFlow.RunAsync();

    [Test]
    public Task AcNativeJournal002SymlinkedJournalRefusesThenOriginalRepairRemainsHealthy()
        => NativeWriteThroughJournalRefusalFlow.RunAsync(NativeJournalEntryKind.Symlink);

    [Test]
    public Task AcNativeJournal002FifoJournalRefusesThenOriginalRepairRemainsHealthy()
        => NativeWriteThroughJournalRefusalFlow.RunAsync(NativeJournalEntryKind.Fifo);

    [Test]
    public Task AcNativeJournal002MissingGuardedJournalNeverCreatesThenOriginalRepairRemainsHealthy()
        => NativeWriteThroughJournalRefusalFlow.RunAsync(NativeJournalEntryKind.Missing);
}
