using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryCapabilityParityFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new QueryCapabilityParityFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                var db = fixture.Harness;
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await QueryCapabilityParityRefusals.RunAsync(fixture, db.Database, db.Store, false, token);
                    await QueryCapabilityParityPages.RunAsync(fixture, db.Database, db.Store, token);
                    await fixture.PolicyAsync(2L, false);
                    await QueryCapabilityParityRefusals.RunAsync(fixture, db.Database, db.Store, true, token);
                }, failures);
                await ServerFailureObserver.ObserveAsync(() => fixture.PolicyAsync(3L, true), failures);
                await ServerFailureObserver.ObserveAsync(() => QueryCapabilityParityPages.RunAsync(fixture, db.Database, db.Store, token), failures);
                await ServerFailureObserver.ObserveAsync(() => QueryCapabilityParityCold.RunAsync(fixture, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
