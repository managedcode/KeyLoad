namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PartitionStorageTests
{
    [Test]
    [NotInParallel]
    public Task ActualStoragePublicationThenLockFailureJoinsAndSameRootsRetainCanonicalReceiptThroughTwoColdStarts()
        => PartitionStorageFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
