namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlLexicalAdmissionCold
{
    private const int ColdOwners = 2;

    internal static async Task RunAsync(TestDatabase database, global::KeyLoad.CommandRequest command, global::KeyLoad.CommitReceipt receipt)
    {
        var identity = database.Store.Identity;
        var before = global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        database.Store.Dispose();
        for (var cut = 0; cut < ColdOwners; cut++)
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = global::KeyLoad.Storage.ZoneTree.ZoneTreeExistingStore.Open(
                    new(database.Directory) { Incarnation = identity.Incarnation }, identity.NodeId, database.StorageExecution);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await SqlJoinSourceBindingFlow.UnchangedAsync(store, before, position, global::KeyLoad.JsonDefaults.Serialize(identity));
                    await SqlLexicalAdmissionFlow.RunAsync(database.CreateConfiguredDatabase(store), store, database.Partition, command, receipt);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
