using KeyLoad.FunctionalTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnLaterChildCancellationTests
{
    private const int Count = 3;

    [Test]
    public async Task ActualLaterChildCancellationDuringRetainedReplayKeepsPendingIntentAndCanonicalAckUntilHealthyResume()
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
        var clock = new QueryObservedWorkClock();
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database, clock);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => AnnLaterChildCancellationAssertions.RunAsync(
                database, actual, request, clock), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        await AnnPendingIntegrityAssertions.ResumeAsync(database, request with
        {
            Mode = AnnMaintenanceMode.Restore
        },
            AnnProjectionPinTestSupport.Tail(database));
    }
}
