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
    private const string ExistingCollection = "orders";

    [Test]
    public async Task UnsupportedVersionRejectsWithoutReadThenRetriesOnSameEngine()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = CreateEngine(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql,
                version: UnsupportedVersion), token);
    }

    [Test]
    public async Task NonNullCursorRejectsWithoutReadThenRetriesOnSameEngine()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = CreateEngine(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql,
                cursor: CursorToken), token);
    }

    [Test]
    public async Task MissingFullScanConsentRejectsWithoutReadThenRetriesOnSameEngine()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = CreateEngine(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql,
                allowFullScan: false), token);
    }

    [Test]
    public async Task ExtraStatementsAndAlternateProjectionGrammarRejectThenRetry()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = CreateEngine(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        var extra = SqlGraphPathTestSupport.LiteralSql + "; SELECT * FROM " + ExistingCollection;
        var alternate = SqlGraphPathTestSupport.LiteralSql.Replace(SelectAll, SelectId, StringComparison.Ordinal);

        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, extra), token);
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, alternate), token);
    }

    [Test]
    public async Task MissingAndWrongScalarParametersRejectThenRetry()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = CreateEngine(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        var missing = SqlGraphPathTestSupport.Parameters((GraphParameter, SqlGraphPathTestSupport.Graph));
        var wrongString = SqlGraphPathTestSupport.Parameters((GraphParameter, new[] { SqlGraphPathTestSupport.Graph }));
        var wrongInteger = SqlGraphPathTestSupport.Parameters(
            (GraphParameter, SqlGraphPathTestSupport.Graph), ("fromCollection", SqlGraphPathTestSupport.Orders),
            ("fromId", SqlGraphPathTestSupport.StartId), ("toCollection", SqlGraphPathTestSupport.Orders),
            ("toId", SqlGraphPathTestSupport.TargetId), (DepthParameter, "2"),
            ("vertices", SqlGraphPathTestSupport.MaximumVertices),
            ("edges", SqlGraphPathTestSupport.MaximumEdges), ("label", SqlGraphPathTestSupport.Label));

        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, ValidSql, missing), token);
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, ValidSql, wrongString), token);
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, ValidSql, wrongInteger), token);
    }

    [Test]
    public async Task CompleteRequestBytesTokensAndParameterCountAreBounded()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var smallRequestEngine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store, new KeyLoad.Security.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new() { MaxQueryBytes = 32 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution()), UnitExecutionOptions.QueryExecution());
        var tokenEngine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store, new KeyLoad.Security.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new() { MaxQueryTokens = 8 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution()), UnitExecutionOptions.QueryExecution());
        var depthEngine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store, new KeyLoad.Security.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new() { MaxQueryDepth = 1 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution()), UnitExecutionOptions.QueryExecution());
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

    private static QueryEngine CreateEngine(TestDatabase database)
        => new(database.Database, UnitExecutionOptions.QueryExecution());

    private static KeyLoadException Failure(QueryEngine engine, SqlGraphPathRequest request)
        => Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPathSql(SqlGraphPathTestSupport.Principal, request,
            cancellationToken: TestContext.Current!.Execution.CancellationToken));
}
