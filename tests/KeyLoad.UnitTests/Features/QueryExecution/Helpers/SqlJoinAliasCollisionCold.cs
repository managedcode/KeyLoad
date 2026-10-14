using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlJoinAliasCollisionCold
{
    private const int ColdCuts = 2;

    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt original)
    {
        var identity = database.Store.Identity;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        database.Store.Dispose();
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            var failures = new List<Exception>();
            try
            {
                using var reopened = ZoneTreeExistingStore.Open(new(database.Directory)
                {
                    Incarnation = identity.Incarnation
                },
                    identity.NodeId, database.StorageExecution);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
                    await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                    await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    var owner = database.CreateConfiguredDatabase(reopened);
                    await SqlJoinAliasCollisionFlow.RunAsync(owner, reopened, database.Partition, repaired: true);
                    var replay = owner.ApplyEmbedded(new(seed.CommandId, OperationKind.Batch, "root", default,
                        JsonSerializer.Serialize(seed, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
                    await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(JsonDefaults.Serialize(original), global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                }, failures);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}

internal sealed record SqlInnerJoinIdentityFault(byte[] Key, byte[] Original, byte[] Damaged, bool LeftFault);

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

internal static class SqlInnerJoinRetainedIdentityPages
{
    private const string Root = "root";
    private const string Left = "orders";
    private const string Right = "customers";
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string FirstLeftId = "a";
    private const string LastLeftId = "z";
    private const string FirstRightId = "c1";
    private const string LastRightId = "c2";
    private const string OrderPath = "/order_id";
    private const string ForeignKeyPath = "/customer_id";
    private const string RightKeyPath = "/id";
    private const string NamePath = "/name";
    private const string TotalPath = "/total";
    private const string OrderField = "order_id";
    private const string NameField = "name";
    private const string GenerationField = "generation";
    private const string GreaterThan = ">";
    private const string AccessPath = "bounded-primary-key-inner-join";
    private const string Corrupt = "The retained relational join source is inconsistent.";
    private const int Dialect = 2;
    private const int PageLimit = 10;
    private const int SingleCandidate = 1;
    private const int FalseThreshold = 100;
    private const long InitialRevision = 1;
    private const string Sql = "SELECT TOP (10) l.order_id AS order_id, r.name AS name, l.total AS generation "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "ORDER BY l.order_id ASC";
    private const string FalseSql = "SELECT TOP (10) l.order_id AS order_id, r.name AS name, l.total AS generation "
        + "FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id "
        + "WHERE l.total > 100 ORDER BY l.order_id ASC";

    private static QueryRequest Request(PartitionRef partition)
        => new(partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect);

    private static AstQueryRequest Ast(PartitionRef partition)
        => new(partition, new SelectQuery(Left, LeftAlias,
            [new(OrderPath, OrderField, LeftAlias), new(NamePath, NameField, RightAlias), new(TotalPath, GenerationField, LeftAlias)],
            null, [new(OrderPath, false)], PageLimit,
            InnerJoin: new(Right, RightAlias, ForeignKeyPath, RightKeyPath)), AllowFullScan: true, AstVersion: Dialect);

    internal static async Task RefuseAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition,
        string[] image, long position, byte[] identity, bool leftFault)
    {
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var sql = Request(partition);
        var ast = Ast(partition);
        var falseAst = ast with
        {
            Query = ast.Query with
            {
                Filter = new Comparison(
            new FieldOperand(TotalPath), GreaterThan, new ValueOperand(
                global::System.Text.Json.JsonSerializer.SerializeToElement(FalseThreshold)))
            }
        };
        var operations = new List<Action>
        {
            () => engine.Execute(Root, sql),
            () => engine.Execute(Root, sql with { Sql = sql.Sql.Replace("TOP (10)", "TOP (1)", StringComparison.Ordinal) }),
            () => engine.ExecuteAst(Root, ast),
            () => engine.ExecuteAst(Root, ast with { Query = ast.Query with { Limit = SingleCandidate } })
        };
        if (leftFault)
        {
            operations.Add(() => engine.Execute(Root, sql with { Sql = FalseSql }));
            operations.Add(() => engine.ExecuteAst(Root, falseAst));
        }
        foreach (var operation in operations)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo(Corrupt);
            await UnchangedAsync(store, image, position, identity);
        }
        if (!leftFault)
        {
            await ExactAsync(engine.Execute(Root, sql with
            {
                Sql = FalseSql
            }), new([], null, position, AccessPath));
            await UnchangedAsync(store, image, position, identity);
            await ExactAsync(engine.ExecuteAst(Root, falseAst), new([], null, position, AccessPath));
            await UnchangedAsync(store, image, position, identity);
        }
    }

    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition,
        CommandRequest seed, CommitReceipt receipt)
    {
        var before = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var expected = new QueryPage(
            [new(FirstLeftId, InitialRevision, "{\"order_id\":\"a\",\"name\":\"7\",\"generation\":7}", false, [],
                [new(LeftAlias, FirstLeftId, InitialRevision), new(RightAlias, FirstRightId, InitialRevision)]),
             new(LastLeftId, InitialRevision, "{\"order_id\":\"z\",\"name\":\"8\",\"generation\":8}", false, [],
                [new(LeftAlias, LastLeftId, InitialRevision), new(RightAlias, LastRightId, InitialRevision)])],
            null, position, AccessPath);
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        await ExactAsync(engine.Execute(Root, Request(partition)), expected);
        await UnchangedAsync(store, before, position, identity);
        await ExactAsync(engine.ExecuteAst(Root, Ast(partition)), expected);
        await UnchangedAsync(store, before, position, identity);
        var replay = owner.ApplyEmbedded(new(seed.CommandId, OperationKind.Batch, Root, default,
            global::System.Text.Json.JsonSerializer.Serialize(seed, JsonDefaults.Options)),
            cancellationToken: default).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await UnchangedAsync(store, before, position, identity);
    }

    internal static async Task UnchangedAsync(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        string[] image, long position, byte[] identity)
    {
        await Assert.That(SqlInnerJoinRetainedIdentityFlow.Image(store))
            .IsEquivalentTo(image, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
    }

    private static async Task ExactAsync(QueryPage actual, QueryPage expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
