using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

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
    private const int Dialect = SqlOperationProtocol.DefaultQueryDialectVersion;
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
        var replay = owner.ApplyEmbedded(owner.CreateCommand(OperationKind.Batch, seed.CommandId, Root, default, global::System.Text.Json.JsonSerializer.Serialize(seed, JsonDefaults.Options)),
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
