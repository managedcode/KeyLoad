namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PartitionHostApplicationLifetimeTests
{
    [Test]
    public Task ConcurrentOpenBorrowAndDisposeRetainOneOwnerAndBothCommittedRowsAcrossTwoReopens()
        => PartitionHostApplicationFlow.RunAsync(heldLock: false);

    [Test]
    public Task HeldNodeLockRejectsMemoizedOpeningThenNewOwnerRecoversTheSameRoot()
        => PartitionHostApplicationFlow.RunAsync(heldLock: true);
}
