using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextOrderedStageCancellationTests
{
    private const int TrackedRecords = 2;

    [Test]
    public async Task SuccessfulNativeBeginThenCanceledLaterReadRetainsStoreAndHealthyBilingualResult()
    {
        TestDatabase? database = null;
        NativeTextMaintenanceTestRuntime? runtime = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                database = new(nativeReplicaAdmission: true);
                database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
                var token = TestContext.Current!.Execution.CancellationToken;
                _ = await NativeTextMaintenanceSeed.CommitAsync(database, token);
                var request = await NativeTextMaintenanceRequestFixture.CreateAsync(database, token);
                var clock = new QueryObservedWorkClock();
                runtime = new(database, clock);
                var begin = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
                await NativeTextLaterStageCancellationAssertions.VerifyAsync(database, runtime, request, begin, clock, token);
                var healthy = await NativeTextMaintenancePhaseFlow.FinishBeganAsync(database, runtime, request, begin, token);
                await Assert.That(healthy.TrackedRecords).IsEqualTo(TrackedRecords);
                await Assert.That(healthy.Checkpoint).IsEqualTo(healthy.ThroughSequence);
                using var projection = NativeTextBilingualAudit.Open(database);
                var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                await ServerFailureObserver.ObserveAsync(
                    () => NativeTextBilingualAudit.VerifyAsync(search, database.Partition, token), failures);
            }, failures);
        }
        finally
        {
            if (runtime is not null)
            { await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures); }
            if (database is not null)
            { ServerFailureObserver.Observe(database.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
