namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Actual execution registration borrows physical ownership without another DI disposal capture.</summary>
internal sealed class PartitionStoragePublicationBorrowTests
{
    [Test]
    public Task UnstartedContainerDisposalPreservesBorrowedOwnerAndCompleteCanonicalCommitAcrossTwoColdOwners()
        => PartitionStoragePublicationBorrowFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
