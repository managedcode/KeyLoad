using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryAdapterCollectionCold
{
    internal static async Task RunAsync(TestDatabase fixture,
        List<(Command<DatabaseCommand> Operation, byte[] Result)> originals, string[] image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlOffsetRefusals.UnchangedAsync(store, image, position, identity);
                await ServerFailureObserver.ObserveAsync(() => QueryAdapterCollectionAssertions.WaveAsync(
                    fixture.CreateConfiguredDatabase(store), store, fixture.Partition, originals), failures);
                await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(
                    store, image, position, identity), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
