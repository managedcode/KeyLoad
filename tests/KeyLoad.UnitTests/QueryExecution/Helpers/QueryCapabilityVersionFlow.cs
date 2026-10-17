using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryCapabilityVersionFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new QueryCapabilityVersionFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                var db = fixture.Harness;
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await QueryCapabilityVersionRefusals.RunAsync(fixture, db.Database, db.Store, false, token);
                    await QueryCapabilityVersionPages.RunAsync(fixture, db.Database, db.Store, token);
                    await fixture.PolicyAsync(2L, false);
                    await QueryCapabilityVersionRefusals.RunAsync(fixture, db.Database, db.Store, true, token);
                }, failures);
                await ServerFailureObserver.ObserveAsync(() => fixture.PolicyAsync(3L, true), failures);
                await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionPages.RunAsync(fixture, db.Database, db.Store, token), failures);
                await ServerFailureObserver.ObserveAsync(() => QueryCapabilityVersionCold.RunAsync(fixture, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
