namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostApplicationLifetimeTests
{
    [Test]
    public Task ConcurrentOpenBorrowAndDisposeRetainOneNativeOwnerAndBothCommittedRowsAcrossTwoReopens()
        => PartitionHostApplicationFlow.RunAsync(heldLock: false);

    [Test]
    public Task HeldNativeNodeLockRejectsMemoizedOpeningThenNewOwnerRecoversTheSameRoot()
        => PartitionHostApplicationFlow.RunAsync(heldLock: true);
}
