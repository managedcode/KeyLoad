namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionStorageNativeApplicationDisposeTests
{
    [Test]
    [NotInParallel]
    public Task ActualGenericApplicationDisposeAfterNativePartialStartupRetainsOwnersThenCanonicalReceiptThroughTwoColdStarts()
        => PartitionStorageNativeApplicationDisposeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
