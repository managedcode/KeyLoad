namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class TransactionalReadViewWholeOperationTests
{
    [Test]
    public Task SnapshotPausedWithinScanAllowsAtomicDocumentIndexWriterThenRefusalCancellationHealthyReplayAndTwoCold()
        => TransactionalReadViewFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
