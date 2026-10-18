namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionStorageNativeProducerDisposeTests
{
    [Test]
    [NotInParallel]
    public Task ActualActiveNativeApplicationDisposeJoinsNativeWorkBeforeSameRootHealthyTwoCold()
        => PartitionStorageNativeProducerDisposeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
