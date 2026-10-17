namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PartitionMovementParentUnpreparedCancellationTests
{
    [Test]
    public Task GenuineCancellationRetainsUnknownBodyThroughColdOwnerThenObservesOwnReceiptAndAdmitsFreshPrepare()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(PartitionMovementParentCancellationTrial.ExecuteAsync);

    [Test]
    public Task GenuinePreparedWinnerDeniesCancellationBeforeJournalAndRetainsObservedBodyAcrossColdOwner()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(PartitionMovementParentPreparedWinnerTrial.ExecuteAsync);

    [Test]
    public Task LostCancellationReplyRetainsQuotaAcrossRevokeRestoreAndColdAdminObservationThenFreshMove()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(PartitionMovementParentCancellationTrial.ExecutePolicyRestoreAsync);

}
