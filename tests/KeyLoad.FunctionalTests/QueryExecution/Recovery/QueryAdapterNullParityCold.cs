using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryAdapterNullParityCold
{
    internal static async Task<(string[] Image, long Position, byte[] Identity)> RunAsync(TestDatabase fixture, int epoch,
        List<(Command<DatabaseCommand> Operation, byte[] Result)> originals,
        (string[] Image, long Position, byte[] Identity) before)
    {
        var failures = new List<Exception>();
        var captured = new List<(string[] Image, long Position, byte[] Identity)>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = fixture.CreateConfiguredDatabase(store);
                await SqlOffsetRefusals.UnchangedAsync(store, before.Image, before.Position, before.Identity);
                await QueryAdapterNullParityFlow.WaveAsync(database, store, fixture.Partition, epoch, originals);
                captured.Add((SqlInnerJoinRetainedIdentityFlow.Image(store), store.Position,
                    JsonDefaults.Serialize(store.Identity)));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return captured.Single();
    }
}
