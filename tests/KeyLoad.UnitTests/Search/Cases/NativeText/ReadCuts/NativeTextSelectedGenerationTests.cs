using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextSelectedGenerationTests
{
    [Test]
    public async Task MaintainedBilingualGenerationRejectsRealUpdateDeleteUntilExplicitRestoreThenReturnsLiteralRows()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(nativeReplicaAdmission: true);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
                var token = TestContext.Current!.Execution.CancellationToken;
                _ = await NativeTextMaintenanceSeed.CommitAsync(database, token);
                var request = await NativeTextMaintenanceRequestFixture.CreateAsync(database, token);
                await using var runtime = new NativeTextMaintenanceTestRuntime(database);
                await ServerFailureObserver.ObserveAsync(
                    () => NativeTextSelectedGenerationFlow.RunAsync(database, runtime, request, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
