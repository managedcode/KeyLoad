using KeyLoad.Clustering;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal static class AnnPendingIntegrityAssertions
{
    private const int Count = 3;

    internal static async Task RejectAsync(TestDatabase database, AnnMaintenanceRequest request)
    {
        var files = AnnFixtureFileSnapshot.Capture(database.Directory);
        await WithOwnerAsync(database, async runtime =>
        {
            var before = AnnMaintenanceTestData.Snapshot(database);
            var position = database.Store.Position;
            var began = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
            _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, began.OriginalCheckpointIntent!);
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Load))
                ?? throw new InvalidOperationException("The truncated actual execution pending image was not rejected.");
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(database.Store.Position).IsEqualTo(position);
        });
        await Assert.That(AnnFixtureFileSnapshot.Capture(database.Directory))
            .IsEquivalentTo(files, CollectionOrdering.Matching);
    }

    internal static async Task ResumeAsync(TestDatabase database, AnnMaintenanceRequest request, long upper)
    {
        string? digest = null;
        await WithOwnerAsync(database, async runtime =>
        {
            var began = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
            _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, began.OriginalCheckpointIntent!);
            _ = await AnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
            var healthy = await AnnMaintenancePhaseAssertions.FinishAsync(database, runtime, request, upper);
            await Assert.That(healthy.Count).IsEqualTo(Count);
            await Assert.That(healthy.Pending).IsFalse();
            digest = healthy.IndexSha256;
            await Assert.That(digest).IsNotNull();
        });
        await AnnPublishedImageAssertions.AssertAsync(database, request, digest!);
    }

    private static async Task WithOwnerAsync(TestDatabase database, Func<AnnMaintenanceTestRuntime, Task> operation)
    {
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => operation(actual), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
