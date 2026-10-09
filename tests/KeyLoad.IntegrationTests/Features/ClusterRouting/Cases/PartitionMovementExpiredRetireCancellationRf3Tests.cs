using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual Aspire-owned original Retire interruption, persisted cancellation and cold reconciliation.</summary>
[NotInParallel]
internal sealed class PartitionMovementExpiredRetireCancellationRf3Tests
{
    [Test]
    public Task ActualSealedRetireExpiryCancelsWithoutEffectsThenColdCancellationAndDistinctCleanupAreHealthy()
        => PartitionMovementExpiredRetireSealedOperationRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task CorruptActualCancelledGrantColdRefusesThenExactStoppedOriginalRestoreIsHealthy()
        => PartitionMovementExpiredRetireCancellationRf3Trial.RunAsync(RequestCqrsProbePhase.SubmitReturned,
            TestContext.Current!.Execution.CancellationToken, corruptDisposition: true);

    [Test]
    public Task LostCancellationReplyColdReconcilesActualReceiptThenDistinctCleanupIsHealthy()
        => PartitionMovementExpiredRetireCancellationRf3Trial.RunAsync(RequestCqrsProbePhase.SubmitReturned,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task UnknownCancellationRetainsFirstAttemptAndQuotaAcrossColdRefusalThenReadsAreHealthy()
        => PartitionMovementExpiredRetireCancellationRf3Trial.RunAsync(RequestCqrsProbePhase.BeforeSubmit,
            TestContext.Current!.Execution.CancellationToken);
}
