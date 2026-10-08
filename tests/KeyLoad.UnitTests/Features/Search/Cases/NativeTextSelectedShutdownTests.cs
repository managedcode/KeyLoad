using KeyLoad.Server;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextSelectedShutdownTests
{
    [Test]
    public async Task ShutdownClosesNewAdmissionAndJoinsActualNativeReaderBeforeColdOwnerReturnsSameLiteralPage()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var clock = new QueryObservedWorkClock();
            using var database = new TestDatabase(timeProvider: clock, nativeReplicaAdmission: true);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
                var token = TestContext.Current!.Execution.CancellationToken;
                _ = await NativeTextMaintenanceSeed.CommitAsync(database, token);
                var request = await NativeTextMaintenanceRequestFixture.CreateAsync(database, token);
                await using var runtime = new NativeTextMaintenanceTestRuntime(database);
                await ServerFailureObserver.ObserveAsync(
                    () => NativeTextSelectedShutdownAssertions.VerifyAsync(database, runtime, request, clock, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
