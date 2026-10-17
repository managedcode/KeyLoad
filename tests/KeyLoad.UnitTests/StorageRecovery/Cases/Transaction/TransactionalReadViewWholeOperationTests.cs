namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class TransactionalReadViewWholeOperationTests
{
    [Test]
    public Task NativeSnapshotPausedWithinScanAllowsAtomicDocumentIndexWriterThenRefusalCancellationHealthyReplayAndTwoCold()
        => TransactionalReadViewFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
