using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphPathRejectionTests
{
    private const string ValidSql = SqlGraphPathTestSupport.ParameterSql;
    private const string GraphParameter = "graph";
    private const string DepthParameter = "depth";
    private const string CursorToken = "cursor";
    private const int UnsupportedVersion = 2;
    private const int MaximumParameters = 257;
    private const string SelectAll = "SELECT *";
    private const string SelectId = "SELECT id";
    private const string SqlGraphWithNestedComment = "/* outer /* nested */ outer */ ";
    private const string Principal = SqlGraphPathTestSupport.Principal;
    private const string ExistingCollection = "orders";

    [Test]
    public async Task InvalidVersionCursorAndFullScanFlagRejectBeforeReturningAPath()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());

        await AssertRejected(engine, SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql,
            version: UnsupportedVersion));
        await AssertRejected(engine, SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql,
            cursor: CursorToken));
        await AssertRejected(engine, SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql, allowFullScan: false));
    }

    [Test]
    public async Task ExtraStatementsAndAlternateProjectionGrammarReject()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var extra = SqlGraphPathTestSupport.LiteralSql + "; SELECT * FROM " + ExistingCollection;
        var alternate = SqlGraphPathTestSupport.LiteralSql.Replace(SelectAll, SelectId, StringComparison.Ordinal);

        await AssertRejected(engine, SqlGraphPathTestSupport.Request(database, extra));
        await AssertRejected(engine, SqlGraphPathTestSupport.Request(database, alternate));
    }

    [Test]
    public async Task MissingAndWrongScalarParametersReject()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var missing = SqlGraphPathTestSupport.Parameters((GraphParameter, SqlGraphPathTestSupport.Graph));
        var wrongString = SqlGraphPathTestSupport.Parameters((GraphParameter, new[] { SqlGraphPathTestSupport.Graph }));
        var wrongInteger = SqlGraphPathTestSupport.Parameters(
            (GraphParameter, SqlGraphPathTestSupport.Graph), ("fromCollection", SqlGraphPathTestSupport.Orders),
            ("fromId", SqlGraphPathTestSupport.StartId), ("toCollection", SqlGraphPathTestSupport.Orders),
            ("toId", SqlGraphPathTestSupport.TargetId), (DepthParameter, "2"),
            ("vertices", SqlGraphPathTestSupport.MaximumVertices),
            ("edges", SqlGraphPathTestSupport.MaximumEdges), ("label", SqlGraphPathTestSupport.Label));

        await AssertRejected(engine, SqlGraphPathTestSupport.Request(database, ValidSql, missing));
        await AssertRejected(engine, SqlGraphPathTestSupport.Request(database, ValidSql, wrongString));
        await AssertRejected(engine, SqlGraphPathTestSupport.Request(database, ValidSql, wrongInteger));
    }

    [Test]
    public async Task CompleteRequestBytesTokensAndParameterCountAreBounded()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var smallRequestEngine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store, new KeyLoad.Security.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new() { MaxQueryBytes = 32 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution()), UnitExecutionOptions.QueryExecution());
        var tokenEngine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store, new KeyLoad.Security.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new() { MaxQueryTokens = 8 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution()), UnitExecutionOptions.QueryExecution());
        var depthEngine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store, new KeyLoad.Security.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new() { MaxQueryDepth = 1 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution()), UnitExecutionOptions.QueryExecution());
        var tooMany = Enumerable.Range(0, MaximumParameters).ToDictionary(index => "p" + index,
            index => JsonSerializer.SerializeToElement(index), StringComparer.Ordinal);
        var byteFailure = Failure(smallRequestEngine, SqlGraphPathTestSupport.Request(database,
            SqlGraphPathTestSupport.LiteralSql));
        var tokenFailure = Failure(tokenEngine, SqlGraphPathTestSupport.Request(database,
            SqlGraphPathTestSupport.LiteralSql));
        var depthSql = SqlGraphWithNestedComment + SqlGraphPathTestSupport.LiteralSql;
        var depthFailure = Failure(depthEngine, SqlGraphPathTestSupport.Request(database, depthSql));
        var parameterFailure = Failure(tokenEngine, SqlGraphPathTestSupport.Request(database,
            SqlGraphPathTestSupport.LiteralSql, tooMany));

        await Assert.That(byteFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(tokenFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(depthFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(parameterFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static async Task AssertRejected(QueryEngine engine, SqlGraphPathRequest request)
        => await Assert.That(Failure(engine, request).Code).IsEqualTo(ErrorCode.Validation);

    private static KeyLoadException Failure(QueryEngine engine, SqlGraphPathRequest request)
        => Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPathSql(Principal, request,
            cancellationToken: TestContext.Current!.Execution.CancellationToken));
}
