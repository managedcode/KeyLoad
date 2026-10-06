using System.Text;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlBetweenBudgetAndAuthorityTests
{
    private const string RangeSql = "SELECT * FROM orders WHERE number BETWEEN 1 AND 9";
    private const string AuthorizedFieldBoundsSql = "SELECT * FROM orders WHERE number BETWEEN lowerBound AND upperBound";
    private const string Reader = "reader";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Partition = "partition";
    private const string Sensitive = "pii";
    private const int ExpandedNodes = 7;
    private const int NegatedNodes = 8;
    private const int ExpandedDepth = 3;
    private const int NegatedDepth = 4;

    [Test]
    public async Task AcSqlc006ALexerBudgetsCountSqlTokensAndRawUtf8BytesSeparatelyFromExpandedAst()
    {
        var byteCount = Encoding.UTF8.GetByteCount(RangeSql);
        var exact = new DatabaseLimits { MaxQueryBytes = byteCount, MaxQueryTokens = 10 };

        await Assert.That(new SqlParser(RangeSql, UnitExecutionOptions.DatabaseLimits(exact), UnitExecutionOptions.QueryExecution()).Parse().Filter).IsTypeOf<Logical>();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser(RangeSql, UnitExecutionOptions.DatabaseLimits(exact with { MaxQueryBytes = byteCount - 1 }), UnitExecutionOptions.QueryExecution()).Parse()).Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => new SqlParser(RangeSql, UnitExecutionOptions.DatabaseLimits(exact with { MaxQueryTokens = 9 }), UnitExecutionOptions.QueryExecution()).Parse()).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcSqlc006AExpandedPositiveAndNegatedTreesMeetExactNormalizerNodeAndDepthLimits()
    {
        var positive = Request(new SqlParser(RangeSql, UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse().Filter!);
        var negative = Request(new SqlParser("SELECT * FROM orders WHERE number NOT BETWEEN 1 AND 9", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse().Filter!);
        var positiveTree = positive.Query.Filter;
        var negativeTree = negative.Query.Filter;

        await Assert.That(JsonDefaults.Serialize(QueryValidation.Normalize(positive, new()
        {
            MaxQueryTokens = ExpandedNodes,
            MaxQueryDepth = ExpandedDepth
        }, UnitExecutionOptions.QueryExecution().Value).Query.Filter).SequenceEqual(JsonDefaults.Serialize(positiveTree))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(QueryValidation.Normalize(negative, new()
        {
            MaxQueryTokens = NegatedNodes,
            MaxQueryDepth = NegatedDepth
        }, UnitExecutionOptions.QueryExecution().Value).Query.Filter).SequenceEqual(JsonDefaults.Serialize(negativeTree))).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => QueryValidation.Normalize(positive,
            new() { MaxQueryTokens = ExpandedNodes - 1, MaxQueryDepth = ExpandedDepth }, UnitExecutionOptions.QueryExecution().Value)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => QueryValidation.Normalize(positive,
            new() { MaxQueryTokens = ExpandedNodes, MaxQueryDepth = ExpandedDepth - 1 }, UnitExecutionOptions.QueryExecution().Value)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => QueryValidation.Normalize(negative,
            new() { MaxQueryTokens = NegatedNodes - 1, MaxQueryDepth = NegatedDepth }, UnitExecutionOptions.QueryExecution().Value)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => QueryValidation.Normalize(negative,
            new() { MaxQueryTokens = NegatedNodes, MaxQueryDepth = NegatedDepth - 1 }, UnitExecutionOptions.QueryExecution().Value)).Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);

        var serializedSize = JsonDefaults.Serialize(QueryValidation.Normalize(positive, new(), UnitExecutionOptions.QueryExecution().Value)).Length;
        await Assert.That(QueryValidation.Normalize(positive, new() { MaxQueryBytes = serializedSize }, UnitExecutionOptions.QueryExecution().Value).Query.Filter)
            .IsNotNull();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => QueryValidation.Normalize(positive,
            new() { MaxQueryBytes = serializedSize - 1 }, UnitExecutionOptions.QueryExecution().Value)).Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcSqlc006ABothBoundFieldsAreAuthorizedBeforeRangeExecution()
    {
        using var database = new TestDatabase();
        database.Configure(SqlBetweenTestData.Collection, ResourceKind.Collection,
            fields: [new("/lowerBound", Sensitive), new("/upperBound", Sensitive)]);
        database.Commit(new PutDocument(SqlBetweenTestData.Collection, SqlBetweenTestData.RowMiddle,
            "{\"number\":5,\"lowerBound\":1,\"upperBound\":9}"));
        ConfigureReader(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var position = database.Store.Position;
        var lowerDenied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader,
            SqlBetweenTestData.Request(database,
                "SELECT * FROM orders WHERE number BETWEEN lowerBound AND 9")));
        var upperDenied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader,
            SqlBetweenTestData.Request(database,
                "SELECT * FROM orders WHERE number BETWEEN 1 AND upperBound")));

        await Assert.That(lowerDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(upperDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(database.Store.Position).IsEqualTo(position);

        var adminControl = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database,
            AuthorizedFieldBoundsSql));

        await Assert.That(SqlBetweenTestData.Ids(adminControl)).IsEqualTo(SqlBetweenTestData.RowMiddle);
        await Assert.That(adminControl.CutPosition).IsEqualTo(position);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task AcSqlc006APreCancelledRangeDoesNotPreventTheNextRequest()
    {
        using var database = SqlBetweenTestData.Create();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, RangeSql), cancellationToken: cancellation.Token));
        var recovered = engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, "SELECT * FROM orders WHERE id = 'middle' AND number BETWEEN 1 AND 9"));

        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(SqlBetweenTestData.Ids(recovered)).IsEqualTo(SqlBetweenTestData.RowMiddle);
    }

    private static AstQueryRequest Request(Predicate filter)
        => new(new(Tenant, Database, SqlBetweenTestData.Collection, Partition),
            new(SqlBetweenTestData.Collection, null, [new("*", "*")], filter, [], 100), AstVersion: 1);

    private static void ConfigureReader(TestDatabase database)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader, Tenant,
            [new(Database, SqlBetweenTestData.Collection, Capability.DocumentsRead | Capability.Query)], [])))
            .Get<PrincipalRecord>();
}
