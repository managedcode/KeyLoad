using System.Collections.Immutable;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnLaterChildCancellationTests
{
    private const int Count = 3;
    private const int Dimensions = 4096;
    internal const int LaterPageLimit = 128;

    [Test]
    public async Task ActualLaterChildCancellationDuringRetainedReplayKeepsPendingIntentAndCanonicalAckUntilHealthyResume()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = AnnSeedTestSupport.Create(Count, dimensions: Dimensions);
            AppendReplayWork(database);
            var actual = database;
            await ServerFailureObserver.ObserveAsync(() => WithRuntimeAsync(actual), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void AppendReplayWork(TestDatabase database)
    {
        var values = new float[Dimensions];
        values[1] = 1f;
        values[2] = -0.5f;
        for (var index = 0; index < LaterPageLimit; index++)
        {
            values[0] = index == LaterPageLimit - 1 ? 0.25f : index + 10.25f;
            AnnSeedTestSupport.CommitVector(database, AnnSeedTestSupport.Id(0),
                ImmutableArray.Create(values), AnnSeedTestSupport.Space(dimension: Dimensions));
        }
    }

    private static async Task WithRuntimeAsync(TestDatabase database)
    {
        var request = AnnMaintenanceTestData.Pin(database) with { Space = AnnSeedTestSupport.Space(dimension: Dimensions) };
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(() => AnnLaterChildCancellationAssertions.RunAsync(
                database, actual, request), failures);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        await AnnPendingIntegrityAssertions.ResumeAsync(database, request with
        {
            Mode = AnnMaintenanceMode.Restore
        },
            AnnProjectionPinTestSupport.Tail(database), LaterPageLimit);
    }
}
