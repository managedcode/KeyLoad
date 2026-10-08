using KeyLoad.Server;
using KeyLoad.UnitTests.Features.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnCaptureShutdownTests
{
    private const int Count = 3;

    [Test]
    public async Task ActualAdmittedNativeSeedCaptureIsJoinedBeforeServiceShutdownThenNewOwnerPublishesHealthy()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = AnnSeedTestSupport.Create(Count);
            var actual = database;
            await ServerFailureObserver.ObserveAsync(() => WithRuntimeAsync(actual), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithRuntimeAsync(TestDatabase database)
    {
        var request = NativeAnnMaintenanceTestData.Pin(database);
        var before = NativeAnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var clock = new QueryObservedWorkClock();
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database, clock);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => NativeAnnCaptureShutdownAssertions.RunAsync(
                database, actual, request, clock), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(NativeAnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await NativeAnnCaptureShutdownAssertions.HealthyAsync(database, request);
    }
}
