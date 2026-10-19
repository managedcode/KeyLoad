namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionStoragePreStartDisposeTests
{
    [Test]
    [NotInParallel]
    public Task ProductionApplicationDisposedBeforeStartNeverOpensPhysicalStorageThenSameRootsSupportCanonicalReplayAndTwoCold()
        => PartitionStoragePreStartDisposeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
