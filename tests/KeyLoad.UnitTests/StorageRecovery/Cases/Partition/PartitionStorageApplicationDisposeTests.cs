namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class PartitionStorageApplicationDisposeTests
{
    [Test]
    [NotInParallel]
    public Task ActualGenericApplicationDisposeAfterPartialStartupRetainsOwnersThenCanonicalReceiptThroughTwoColdStarts()
        => PartitionStorageApplicationDisposeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
