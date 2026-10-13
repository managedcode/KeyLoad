namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlFetchCountCold
{
    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt receipt)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        database.Store.Dispose();
        var failures = new List<Exception>();
        for (var cold = 0; cold < SqlFetchCountProtocol.ColdCuts; cold++)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = global::KeyLoad.Storage.ZoneTree.ZoneTreeExistingStore.Open(
                    new(database.Directory) { Incarnation = database.Store.Identity.Incarnation },
                    database.Store.Identity.NodeId, database.StorageExecution);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await SqlFetchCountRefusals.UnchangedAsync(store, image, position, identity);
                    var owner = database.CreateConfiguredDatabase(store);
                    await SqlFetchCountPages.HealthyAsync(owner, store, database.Partition, SqlFetchCountProtocol.Reader);
                    await SqlFetchCountPages.ReplayAsync(owner, seed, receipt);
                    await SqlFetchCountRefusals.UnchangedAsync(store, image, position, identity);
                }, failures);
            }, failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
