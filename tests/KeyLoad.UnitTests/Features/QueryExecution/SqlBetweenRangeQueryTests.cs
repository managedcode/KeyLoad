using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlBetweenRangeQueryTests
{
    private const string InclusiveSql = "SELECT * FROM orders WHERE number BETWEEN 1 AND 9 ORDER BY id";
    private const string ReversedSql = "SELECT * FROM orders WHERE number BETWEEN 9 AND 1 ORDER BY id";
    private const string ParameterSql = "SELECT * FROM orders WHERE number BETWEEN @lower AND @upper ORDER BY id";
    private const string StringSql = "SELECT * FROM orders WHERE label BETWEEN 'amber' AND 'moss' ORDER BY id";
    private const string BooleanSql = "SELECT * FROM orders WHERE flag BETWEEN FALSE AND TRUE ORDER BY id";
    private const string FieldBoundSql = "SELECT * FROM orders WHERE number BETWEEN lowerBound AND upperBound ORDER BY id";
    private const string QuotedFieldSql = "SELECT * FROM orders o WHERE o.\"odd.name\" /* before */ BETWEEN /* bounds */ 1 AND 9 ORDER BY id";

    [Test]
    public async Task AcSqlc006ARealZoneTreeQueryIncludesBothBoundsAndUsesIndependentAst()
    {
        using var database = SqlBetweenTestData.Create();
        var engine = new QueryEngine(database.Database);
        var sqlPage = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, InclusiveSql));
        var expected = new AstQueryRequest(database.Partition,
            new(SqlBetweenTestData.Collection, null, [new("*", "*")], Range(new FieldOperand(SqlBetweenTestData.NumberPath),
                ValueOperand.Create(1m), ValueOperand.Create(9m)), [new(SqlBetweenTestData.IdPath, false)], 100));
        var astPage = engine.ExecuteAst(SqlBetweenTestData.Root, expected with { AllowFullScan = true });

        await Assert.That(SqlBetweenTestData.Ids(sqlPage)).IsEqualTo("high,low,middle");
        await SqlBetweenTestData.SameRows(astPage, sqlPage);
    }

    [Test]
    public async Task AcSqlc006AReversedBoundsAreValidAndMatchNoNumericRows()
    {
        using var database = SqlBetweenTestData.Create();
        var page = new QueryEngine(database.Database).Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, ReversedSql));

        await Assert.That(page.Rows).IsEmpty();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-full-scan");
    }

    [Test]
    public async Task AcSqlc006AFullScanRequiresExplicitConsentAndQuotedAliasedRangeExecutesWithComments()
    {
        using var database = SqlBetweenTestData.Create();
        database.Commit(new PutDocument(SqlBetweenTestData.Collection, SqlBetweenTestData.RowQuotedField,
            "{\"odd.name\":5}"));
        var engine = new QueryEngine(database.Database);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlBetweenTestData.Root,
            new(database.Partition, QuotedFieldSql)));
        var optedIn = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, QuotedFieldSql));

        await Assert.That(denied.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(SqlBetweenTestData.Ids(optedIn)).IsEqualTo(SqlBetweenTestData.RowQuotedField);
    }

    [Test]
    public async Task AcSqlc006AParametersStringAndBooleanBoundsUseExistingScalarOrdering()
    {
        using var database = SqlBetweenTestData.Create();
        var engine = new QueryEngine(database.Database);
        var parameterPage = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, ParameterSql,
            SqlBetweenTestData.Bounds(1, 9)));
        var stringPage = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, StringSql));
        var booleanPage = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database, BooleanSql));

        await Assert.That(SqlBetweenTestData.Ids(parameterPage)).IsEqualTo("high,low,middle");
        await Assert.That(SqlBetweenTestData.Ids(stringPage)).IsEqualTo("label-row,low,middle");
        await Assert.That(SqlBetweenTestData.Ids(booleanPage)).IsEqualTo("flag-row,high,low,middle");
    }

    [Test]
    public async Task AcSqlc006AFieldBoundsResolveOnTheSameDocument()
    {
        using var database = SqlBetweenTestData.Create();
        database.Commit(new PutDocument(SqlBetweenTestData.Collection, SqlBetweenTestData.RowFieldBounds,
            SqlBetweenTestData.FieldBoundsDocument));
        var page = new QueryEngine(database.Database).Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, FieldBoundSql));

        await Assert.That(SqlBetweenTestData.Ids(page)).IsEqualTo(SqlBetweenTestData.RowFieldBounds);
    }

    [Test]
    public async Task AcSqlc006ASqlAndAstKeepCursorAccessPathAndExplainEquivalent()
    {
        using var database = SqlBetweenTestData.Create();
        var engine = new QueryEngine(database.Database);
        var sql = engine.Execute(SqlBetweenTestData.Root,
            SqlBetweenTestData.Request(database, "SELECT * FROM orders WHERE number BETWEEN 1 AND 9 ORDER BY id LIMIT 2"));
        var sqlNext = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database,
            "SELECT * FROM orders WHERE number BETWEEN 1 AND 9 ORDER BY id LIMIT 2") with
        { Cursor = sql.Cursor });
        var astRequest = new AstQueryRequest(database.Partition,
            new(SqlBetweenTestData.Collection, null, [new("*", "*")], Range(new FieldOperand(SqlBetweenTestData.NumberPath),
                ValueOperand.Create(1m), ValueOperand.Create(9m)), [new(SqlBetweenTestData.IdPath, false)], 2), AllowFullScan: true);
        var ast = engine.ExecuteAst(SqlBetweenTestData.Root, astRequest);
        var sqlCursorInAst = engine.ExecuteAst(SqlBetweenTestData.Root, astRequest with { Cursor = sql.Cursor });
        var astCursorInSql = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database,
            "SELECT * FROM orders WHERE number BETWEEN 1 AND 9 ORDER BY id LIMIT 2") with
        { Cursor = ast.Cursor });

        await SqlBetweenTestData.SameRows(ast, sql);
        await SqlBetweenTestData.SameRows(sqlCursorInAst, sqlNext);
        await SqlBetweenTestData.SameRows(astCursorInSql, sqlNext);
        await Assert.That(SqlBetweenTestData.Ids(sql)).IsEqualTo("high,low");
        await Assert.That(SqlBetweenTestData.Ids(sqlNext)).IsEqualTo("middle");
        await Assert.That(sql.Cursor).IsNotNull();
        await Assert.That(sqlNext.Cursor).IsNull();

        var sqlExplain = engine.Execute(SqlBetweenTestData.Root, SqlBetweenTestData.Request(database,
            "EXPLAIN SELECT * FROM orders WHERE number BETWEEN 1 AND 9 ORDER BY id LIMIT 2"));
        var astExplain = engine.ExecuteAst(SqlBetweenTestData.Root, new AstQueryRequest(database.Partition,
            new(SqlBetweenTestData.Collection, null, [new("*", "*")], Range(new FieldOperand(SqlBetweenTestData.NumberPath),
                ValueOperand.Create(1m), ValueOperand.Create(9m)), [new(SqlBetweenTestData.IdPath, false)], 2, Explain: true),
            AllowFullScan: true));
        await Assert.That(astExplain.AccessPath).IsEqualTo(sqlExplain.AccessPath);
        await Assert.That(astExplain.Rows[0].Json).IsEqualTo(sqlExplain.Rows[0].Json);
    }

    private static Logical Range(Operand value, Operand lower, Operand upper)
        => new(new Comparison(value, ">=", lower), "AND", new Comparison(value, "<=", upper));
}
