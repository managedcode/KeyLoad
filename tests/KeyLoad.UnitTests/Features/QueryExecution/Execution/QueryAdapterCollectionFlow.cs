using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterCollectionFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var originals = await QueryAdapterCollectionFixture.SeedAsync(fixture);
                await QueryAdapterCollectionAssertions.WaveAsync(fixture.Database, fixture.Store, fixture.Partition, originals);
                var image = SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store);
                var position = fixture.Store.Position;
                var identity = JsonDefaults.Serialize(fixture.Store.Identity);
                fixture.Store.Dispose();
                for (var cold = 0; cold < 2; cold++)
                {
                    await QueryAdapterCollectionCold.RunAsync(fixture, originals, image, position, identity);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
