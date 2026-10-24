namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlBetweenSymmetricCold
{
    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt receipt)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var storeIdentity = database.Store.Identity;
        database.Store.Dispose();
        var failures = new List<Exception>();
        for (var cold = 0; cold < SqlNullOrderingProtocol.ColdCuts; cold++)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = global::KeyLoad.Storage.ZoneTree.ZoneTreeExistingStore.Open(
                    new(database.Directory)
                    {
                        Incarnation = storeIdentity.Incarnation
                    }, storeIdentity.NodeId, database.StorageExecution);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var owner = database.CreateConfiguredDatabase(store);
                    await SqlBetweenSymmetricPages.HealthyAsync(owner, store, database.Partition, SqlBetweenSymmetricRefusals.RepairedRevision);
                    await SqlNullOrderingPages.ReplayAsync(owner, seed, receipt);
                    await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
                }, failures);
            }, failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
