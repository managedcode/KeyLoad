using KeyLoad.Server;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnLaterChildCancellationTests
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
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithRuntimeAsync(TestDatabase database)
    {
        var request = NativeAnnMaintenanceTestData.Pin(database);
        var clock = new QueryObservedWorkClock();
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database, clock);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => NativeAnnLaterChildCancellationAssertions.RunAsync(
                database, actual, request, clock), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        await NativeAnnPendingIntegrityAssertions.ResumeAsync(database, request with { Mode = AnnMaintenanceMode.Restore },
            AnnProjectionPinTestSupport.Tail(database));
    }
}
