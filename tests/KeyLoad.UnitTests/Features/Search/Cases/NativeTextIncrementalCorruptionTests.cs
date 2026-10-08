using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextIncrementalCorruptionTests
{
    [Test]
    public async Task DamagedNativeManifestRejectsWithoutCanonicalEffectsThenExactRepairRestoresHealthy()
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
                runtime = new(database);
                _ = await NativeTextMaintenancePhaseFlow.FinishAsync(database, runtime, request, token);
                await runtime.DisposeAsync();
                runtime = null;
                await NativeTextIncrementalCorruptionAssertions.VerifyAsync(database, request, token);
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
