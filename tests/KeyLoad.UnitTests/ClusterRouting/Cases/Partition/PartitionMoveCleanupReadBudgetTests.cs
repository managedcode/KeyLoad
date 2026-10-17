using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionMoveCleanupReadBudgetTests
{
    [Test]
    public async Task NativeCleanupLookaheadAndOwnedCopyAdmissionPreserveWholeImageThroughTwoColdCuts()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PartitionRecordNativeFixture();
            await ServerFailureObserver.ObserveAsync(() => PartitionMoveCleanupReadBudgetTrial.RunAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
