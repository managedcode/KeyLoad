using KeyLoad.Server;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextSelectedShutdownTests
{
    [Test]
    public async Task ShutdownClosesNewAdmissionAndJoinsActualReaderBeforeColdOwnerReturnsSameLiteralPage()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var clock = new QueryObservedWorkClock();
            using var database = new TestDatabase(timeProvider: clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
                var token = TestContext.Current!.Execution.CancellationToken;
                _ = await TextMaintenanceSeed.CommitAsync(database, token);
                var request = await TextMaintenanceRequestFixture.CreateAsync(database, token);
                await using var runtime = new TextMaintenanceTestRuntime(database);
                await ServerFailureObserver.ObserveAsync(
                    () => TextSelectedShutdownAssertions.VerifyAsync(database, runtime, request, clock, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
