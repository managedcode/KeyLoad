using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlModelEventRecordIntegrityCold
{
    private const int ColdCuts = 2;
    internal static async Task RunAsync(TestDatabase database, byte[] key, byte[] original, EventRecord[] damaged)
    {
        foreach (var record in damaged)
        {
            await FaultAsync(database, key, original, OrleansSerialization.Serialize(record));
        }
    }

    private static async Task FaultAsync(TestDatabase database, byte[] key, byte[] original, byte[] damaged)
    {
        var failures = new List<Exception>();
        string[] before = [];
        long position = 0;
        byte[] identity = [];
        var committed = false;
        var prefix = Convert.ToHexString(key) + ":";
        var originalRow = prefix + Convert.ToHexString(original);
        await InOwnerAsync(database, async store =>
        {
            before = QueueWholeFlowStorage.Bytes(store);
            position = store.Position;
            identity = JsonDefaults.Serialize(store.Identity);
            await Assert.That(before.Single(row => row.StartsWith(prefix, StringComparison.Ordinal))).IsEqualTo(originalRow);
            store.Commit((transaction, _) => { transaction.Put(key, damaged); return true; });
            committed = true;
        }, failures);
        if (!committed)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        var expected = before.Select(row => row == originalRow
            ? prefix + Convert.ToHexString(damaged) : row).ToArray();
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            await InOwnerAsync(database, async cold =>
            {
                await Assert.That(JsonDefaults.Serialize(cold.Identity)).IsEquivalentTo(identity, CollectionOrdering.Matching);
                await SqlModelEventRecordIntegrityFlow.QueriesAsync(cold, database.CreateConfiguredDatabase(cold),
                    database.Partition, expected, checked(position + 1));
            }, failures);
        }
        await InOwnerAsync(database, repair =>
        {
            repair.Commit((transaction, _) => { transaction.Put(key, original); return true; });
            return Task.CompletedTask;
        }, failures);
        await InOwnerAsync(database, async healthy =>
        {
            await Assert.That(QueueWholeFlowStorage.Bytes(healthy)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(healthy.Position).IsEqualTo(checked(position + 2));
            await Assert.That(JsonDefaults.Serialize(healthy.Identity)).IsEquivalentTo(identity, CollectionOrdering.Matching);
            var engine = new QueryEngine(database.CreateConfiguredDatabase(healthy), UnitExecutionOptions.QueryExecution());
            await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute("root",
                SqlEventReferenceJoinFixture.Request(database.Partition)), healthy.Position, repaired: true);
            await SqlEventReferenceJoinAssertions.PageAsync(engine.ExecuteAst("root",
                SqlEventReferenceJoinFixture.Ast(database.Partition)), healthy.Position, repaired: true);
            await Assert.That(QueueWholeFlowStorage.Bytes(healthy)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(healthy.Position).IsEqualTo(checked(position + 2));
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task InOwnerAsync(TestDatabase database,
        Func<global::KeyLoad.Storage.ZoneTree.ZoneTreeStore, Task> use, List<Exception> failures)
    {
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore? store = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            store = Open(database);
            await use(store);
        }, failures);
        if (store is not null)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
    }

    private static global::KeyLoad.Storage.ZoneTree.ZoneTreeStore Open(TestDatabase database)
        => ZoneTreeExistingStore.Open(new(database.Directory)
        {
            Incarnation = database.Store.Identity.Incarnation
        },
            database.Store.Identity.NodeId, database.StorageExecution);
}
