using KeyLoad.Server;
using KeyLoad.UnitTests.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal sealed class AnnCaptureShutdownTests
{
    private const int Count = 3;

    [Test]
    public async Task ActualAdmittedSeedCaptureIsJoinedBeforeServiceShutdownThenNewOwnerPublishesHealthy()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = AnnSeedTestSupport.Create(Count);
            var actual = database;
            await ServerFailureObserver.ObserveAsync(() => WithRuntimeAsync(actual), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithRuntimeAsync(TestDatabase database)
    {
        var request = AnnMaintenanceTestData.Pin(database);
        var before = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        var clock = new QueryObservedWorkClock();
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database, clock);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => AnnCaptureShutdownAssertions.RunAsync(
                database, actual, request, clock), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await AnnCaptureShutdownAssertions.HealthyAsync(database, request);
    }
}
