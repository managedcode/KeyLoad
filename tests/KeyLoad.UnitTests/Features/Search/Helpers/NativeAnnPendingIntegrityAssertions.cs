using KeyLoad.Orleans;
using KeyLoad.Server;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnPendingIntegrityAssertions
{
    private const int Count = 3;

    internal static Task RejectAsync(TestDatabase database, AnnMaintenanceRequest request)
        => WithOwnerAsync(database, async runtime =>
        {
            var before = NativeAnnMaintenanceTestData.Snapshot(database);
            var position = database.Store.Position;
            var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
            _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, began.OriginalCheckpointIntent!);
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => runtime.PhaseAsync(database, request, AnnMaintenanceCapabilityKind.Load))
                ?? throw new InvalidOperationException("The truncated actual native pending image was not rejected.");
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(database.Store.Position).IsEqualTo(position);
        });

    internal static async Task ResumeAsync(TestDatabase database, AnnMaintenanceRequest request, long upper)
    {
        string? digest = null;
        await WithOwnerAsync(database, async runtime =>
        {
            var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Begin);
            _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, began.OriginalCheckpointIntent!);
            _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, runtime, request, AnnMaintenanceCapabilityKind.Load);
            var healthy = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, request, upper);
            await Assert.That(healthy.Count).IsEqualTo(Count);
            await Assert.That(healthy.Pending).IsFalse();
            digest = healthy.IndexSha256;
            await Assert.That(digest).IsNotNull();
        });
        await NativeAnnPublishedImageAssertions.AssertAsync(database, request, digest!);
    }

    private static async Task WithOwnerAsync(TestDatabase database, Func<NativeAnnMaintenanceTestRuntime, Task> operation)
    {
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => operation(actual), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
