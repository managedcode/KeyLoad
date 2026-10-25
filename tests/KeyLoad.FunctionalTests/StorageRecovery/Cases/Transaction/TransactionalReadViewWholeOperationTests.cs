namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class TransactionalReadViewWholeOperationTests
{
    [Test]
    public Task SnapshotPausedWithinScanAllowsAtomicDocumentIndexWriterThenRefusalCancellationHealthyReplayAndTwoCold()
        => TransactionalReadViewFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
