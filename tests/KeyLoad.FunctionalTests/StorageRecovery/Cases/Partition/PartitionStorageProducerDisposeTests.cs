namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PartitionStorageProducerDisposeTests
{
    [Test]
    [NotInParallel]
    public Task ActualActiveApplicationDisposeJoinsWorkBeforeSameRootHealthyTwoCold()
        => PartitionStorageProducerDisposeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
