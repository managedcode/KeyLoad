using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextOrderedStageCancellationTests
{
    private const int TrackedRecords = 2;

    [Test]
    public async Task SuccessfulBeginThenCanceledLaterReadRetainsStoreAndHealthyBilingualResult()
    {
        TestDatabase? database = null;
        TextMaintenanceTestRuntime? runtime = null;
        ReadObservedStore? observed = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                database = new();
                database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
                var token = TestContext.Current!.Execution.CancellationToken;
                _ = await TextMaintenanceSeed.CommitAsync(database, token);
                var request = await TextMaintenanceRequestFixture.CreateAsync(database, token);
                observed = new(database.Store);
                runtime = new(database.CreateConfiguredDatabase(observed), database.Directory);
                var begin = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
                await TextLaterStageCancellationAssertions.VerifyAsync(database, runtime, request, begin, observed, token);
                var healthy = await TextMaintenancePhaseFlow.FinishBeganAsync(database, runtime, request, begin, token);
                await Assert.That(healthy.TrackedRecords).IsEqualTo(TrackedRecords);
                await Assert.That(healthy.Checkpoint).IsEqualTo(healthy.ThroughSequence);
                using var projection = TextBilingualAudit.Open(database);
                var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                await ServerFailureObserver.ObserveAsync(
                    () => TextBilingualAudit.VerifyAsync(search, database, token), failures);
            }, failures);
        }
        finally
        {
            if (runtime is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures);
            }
            if (database is not null)
            {
                observed?.Dispose();
                ServerFailureObserver.Observe(database.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
