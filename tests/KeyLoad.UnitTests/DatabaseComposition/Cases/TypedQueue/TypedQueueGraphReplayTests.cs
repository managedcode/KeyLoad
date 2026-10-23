namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class TypedQueueGraphReplayTests
{
    private const int IdFault = 2;

    [Test]
    public async Task TypedSavedEndpointFaultRefusesOriginalReplayThenRepairsAcrossDirtyAndHealthyColdOwners()
    {
        foreach (var from in new[] { true, false })
        {
            foreach (var component in new[] { 0, 1, IdFault })
            {
                var failures = new List<Exception>();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var db = new TestDatabase();
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                    {
                        var source = await TypedQueueGraphReplayFixture.SeedAsync(db, from);
                        await QueueGraphReplayFlow.RunSeededAsync(source, from, component);
                    }, failures);
                }, failures);
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            }
        }
    }
}
