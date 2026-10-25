using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextIncrementalCorruptionTests
{
    [Test]
    public async Task DamagedManifestRejectsWithoutCanonicalEffectsThenExactRepairRestoresHealthy()
    {
        TestDatabase? database = null;
        TextMaintenanceTestRuntime? runtime = null;
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
                runtime = new(database);
                _ = await TextMaintenancePhaseFlow.FinishAsync(database, runtime, request, token);
                await runtime.DisposeAsync();
                runtime = null;
                await TextIncrementalCorruptionAssertions.VerifyAsync(database, request, token);
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
                ServerFailureObserver.Observe(database.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
