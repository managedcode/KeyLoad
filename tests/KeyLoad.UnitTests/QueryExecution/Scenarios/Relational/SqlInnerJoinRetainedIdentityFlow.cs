namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlInnerJoinRetainedIdentityFlow
{
    private const string Left = "orders";
    private const string Right = "customers";
    private const string LeftId = "z";
    private const string RightId = "c2";
    private const string Foreign = "foreign";
    private const int ColdCuts = 2;
    private const long FaultCommit = 1;
    private const long RepairCommit = 2;

    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt receipt)
    {
        var faults = Capture(database);
        await SqlInnerJoinRetainedIdentityPages.HealthyAsync(database.Database, database.Store,
            database.Partition, seed, receipt);
        database.Store.Dispose();
        foreach (var fault in faults)
        {
            await FaultAsync(database, seed, receipt, fault);
        }
    }

    private static SqlInnerJoinIdentityFault[] Capture(TestDatabase database)
    {
        var faults = new List<SqlInnerJoinIdentityFault>();
        foreach (var left in new[] { true, false })
        {
            var collection = left ? Left : Right;
            var id = left ? LeftId : RightId;
            var key = global::KeyLoad.Core.Features.DocumentStorage.DocumentStorageKeys.RecordKey(
                database.Partition, collection, id);
            var bytes = database.Store.Read(view => view.ReadOwnedValue(key))
                        ?? throw new InvalidOperationException("The original retained join row is absent.");
            var row = NativeSerialization.Deserialize<DocumentRecord>(bytes);
            var foreignPartition = new PartitionRef(database.Partition.TenantId, database.Partition.DatabaseId,
                database.Partition.TransactionDomainId, Foreign);
            var identities = new EntityRef[]
            {
                new(foreignPartition, collection, id),
                new(database.Partition, Foreign, id),
                new(database.Partition, collection, Foreign)
            };
            foreach (var identity in identities)
            {
                faults.Add(new(key, bytes, NativeSerialization.Serialize(row with
                {
                    Reference = identity
                }), left));
            }
        }
        return faults.ToArray();
    }

    internal static async Task FaultAsync(TestDatabase database, CommandRequest seed,
        CommitReceipt receipt, SqlInnerJoinIdentityFault fault)
    {
        var failures = new List<Exception>();
        string[] before = [];
        byte[] identity = [];
        long position = 0;
        var committed = false;
        var prefix = Convert.ToHexString(fault.Key) + ":";
        var original = prefix + Convert.ToHexString(fault.Original);
        string[] damaged = [];
        await InOwnerAsync(database, async store =>
        {
            before = Image(store);
            identity = JsonDefaults.Serialize(store.Identity);
            position = store.Position;
            await Assert.That(before.Single(row => row.StartsWith(prefix, StringComparison.Ordinal))).IsEqualTo(original);
            damaged = before.Select(row => row == original ? prefix + Convert.ToHexString(fault.Damaged) : row).ToArray();
            store.Commit((transaction, _) => { transaction.Put(fault.Key, fault.Damaged); return true; });
            committed = true;
            await SqlInnerJoinRetainedIdentityPages.RefuseAsync(database.CreateConfiguredDatabase(store), store,
                database.Partition, damaged, checked(position + FaultCommit), identity, fault.LeftFault);
        }, failures);
        if (!committed)
        {
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await DirtyAsync(database, fault, damaged, checked(position + FaultCommit), identity, failures);
        await InOwnerAsync(database, store =>
        {
            store.Commit((transaction, _) => { transaction.Put(fault.Key, fault.Original); return true; });
            return Task.CompletedTask;
        }, failures);
        await HealthyColdAsync(database, seed, receipt, before, checked(position + RepairCommit), identity, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DirtyAsync(TestDatabase database, SqlInnerJoinIdentityFault fault,
        string[] damaged, long position, byte[] identity, List<Exception> failures)
    {
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            await InOwnerAsync(database, store => SqlInnerJoinRetainedIdentityPages.RefuseAsync(
                database.CreateConfiguredDatabase(store), store, database.Partition,
                damaged, position, identity, fault.LeftFault), failures);
        }
    }

    private static async Task HealthyColdAsync(TestDatabase database, CommandRequest seed,
        CommitReceipt receipt, string[] before, long position, byte[] identity, List<Exception> failures)
    {
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            await InOwnerAsync(database, async store =>
            {
                await SqlInnerJoinRetainedIdentityPages.UnchangedAsync(store, before, position, identity);
                await SqlInnerJoinRetainedIdentityPages.HealthyAsync(database.CreateConfiguredDatabase(store),
                    store, database.Partition, seed, receipt);
                await SqlInnerJoinRetainedIdentityPages.UnchangedAsync(store, before, position, identity);
            }, failures);
        }
    }

    private static async Task InOwnerAsync(TestDatabase database,
        Func<global::KeyLoad.Storage.ZoneTree.ZoneTreeStore, Task> use, List<Exception> failures)
    {
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore? store = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            store = global::KeyLoad.Storage.ZoneTree.ZoneTreeExistingStore.Open(
                new(database.Directory)
                {
                    Incarnation = database.Store.Identity.Incarnation
                },
                database.Store.Identity.NodeId, database.StorageExecution);
            await use(store);
        }, failures);
        if (store is not null)
        {
            global::KeyLoad.Server.ServerFailureObserver.Observe(store.Dispose, failures);
        }
    }

    internal static string[] Image(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store)
        => global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(store);
}
