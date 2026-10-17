namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionStorageNativePreStartDisposeTests
{
    [Test]
    [NotInParallel]
    public Task ProductionApplicationDisposedBeforeStartNeverOpensPhysicalStorageThenSameRootsSupportSignedReplayAndTwoCold()
        => PartitionStorageNativePreStartDisposeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
