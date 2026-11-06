using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextSelectedShutdownTests
{
    [Test]
    public async Task ShutdownClosesNewAdmissionAndJoinsActualReaderBeforeColdOwnerReturnsSameLiteralPage()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
                var token = TestContext.Current!.Execution.CancellationToken;
                _ = await TextMaintenanceSeed.CommitAsync(database, token);
                var request = await TextMaintenanceRequestFixture.CreateAsync(database, token);
                await using var runtime = new TextMaintenanceTestRuntime(database);
                await ServerFailureObserver.ObserveAsync(
                    () => TextSelectedShutdownAssertions.VerifyAsync(database, runtime, request, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
