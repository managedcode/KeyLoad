namespace KeyLoad.UnitTests.QueryExecution;

internal static class ScriptDomSelectCold
{
    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt receipt)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = database.Store.Identity;
        var identityBytes = JsonDefaults.Serialize(identity);
        database.Store.Dispose();
        var failures = new List<Exception>();
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = global::KeyLoad.Storage.ZoneTree.ZoneTreeExistingStore.Open(
                    new(database.Directory)
                    {
                        Incarnation = identity.Incarnation
                    }, identity.NodeId, database.StorageExecution);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identityBytes);
                    var owner = database.CreateConfiguredDatabase(store);
                    await ScriptDomSelectPages.HealthyAsync(owner, store, database.Partition);
                    await SqlOffsetPages.ReplayAsync(owner, seed, receipt);
                    await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identityBytes);
                }, failures);
            }, failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
    private const int ColdOwners = 2;
}
