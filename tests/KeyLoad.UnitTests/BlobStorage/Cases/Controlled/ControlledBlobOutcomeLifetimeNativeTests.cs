using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

[NotInParallel]
internal sealed class ControlledBlobOutcomeLifetimeNativeTests
{
    [Test]
    public Task GenuineSixKindOutcomeCoverageRefusalsAndTwoColdOwnersPreserveOriginalReceiptsAtEightKiB()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            try
            {
                await Assert.That(source.Database.Limits.MaxBatchBytes)
                    .IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
                await Assert.That(target.Database.Limits.MaxBatchBytes)
                    .IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
                await ControlledBlobOutcomeLifetimeNativeFlow.ExecuteAsync(source, target, listeners, corpus,
                    TestContext.Current!.Execution.CancellationToken);
            }
            catch (Exception)
            {
                source.RetainRoot = true;
                target.RetainRoot = true;
                throw;
            }
        }, limits: Limits(), sourceHistoryEntries: ControlledPartitionMovementNativeJournal.BlobOutcomeSourceHistoryEntries,
            targetHistoryEntries: ControlledPartitionMovementNativeJournal.BlobOutcomeTargetHistoryEntries);

    private static DatabaseLimits Limits()
    {
        var limits = new DatabaseLimits { MaxBatchBytes = NativeControlledLifetimePayloadBoundary.MaximumBytes };
        limits.Validate();
        return limits;
    }
}
