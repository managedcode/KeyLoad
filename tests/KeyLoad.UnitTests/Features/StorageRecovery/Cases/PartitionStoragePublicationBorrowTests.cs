namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Actual native registration borrows physical ownership without another DI disposal capture.</summary>
internal sealed class PartitionStoragePublicationBorrowTests
{
    [Test]
    public Task UnstartedNativeContainerDisposalPreservesBorrowedOwnerAndCompleteCanonicalCommitAcrossTwoColdOwners()
        => PartitionStoragePublicationBorrowFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
