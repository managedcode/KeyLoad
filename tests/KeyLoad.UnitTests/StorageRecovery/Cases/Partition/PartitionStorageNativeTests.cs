namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionStorageNativeTests
{
    [Test]
    [NotInParallel]
    public Task ActualNativeStoragePublicationThenLockFailureJoinsAndSameRootsRetainSignedReceiptThroughTwoColdStarts()
        => PartitionStorageNativeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
