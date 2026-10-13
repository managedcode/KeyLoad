namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlOffsetCold
{
    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt receipt)
    {
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        database.Store.Dispose();
        var failures = new List<Exception>();
        for (var cold = 0; cold < SqlOffsetProtocol.ColdCuts; cold++)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = global::KeyLoad.Storage.ZoneTree.ZoneTreeExistingStore.Open(
                    new(database.Directory) { Incarnation = database.Store.Identity.Incarnation },
                    database.Store.Identity.NodeId, database.StorageExecution);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await SqlOffsetRefusals.UnchangedAsync(store, before, position, identity);
                    var owner = database.CreateConfiguredDatabase(store);
                    await SqlOffsetPages.HealthyAsync(owner, store, database.Partition, SqlOffsetProtocol.Reader);
                    await SqlOffsetPages.ReplayAsync(owner, seed, receipt);
                    await SqlOffsetRefusals.UnchangedAsync(store, before, position, identity);
                }, failures);
            }, failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
