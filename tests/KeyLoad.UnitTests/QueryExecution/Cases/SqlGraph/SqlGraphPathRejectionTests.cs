using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

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
    private const string ExistingCollection = "orders";
    private const string UnsupportedDetail = "The SQL statement or execution clause is unsupported.";
    private const string ParameterDetail = "A named SQL graph-path parameter is missing or has the wrong scalar type.";

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
            SqlGraphPathTestSupport.Request(database, extra), ErrorCode.UnsupportedCapability, UnsupportedDetail, token);
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, alternate), ErrorCode.UnsupportedCapability, UnsupportedDetail, token);
    }

    [Test]
    public async Task MissingAndWrongScalarParametersRejectThenRetry()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = CreateEngine(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await ScriptDomOrdinalGraphAdmission.RunAsync(database, engine, token);
        var missing = SqlGraphPathTestSupport.Parameters((GraphParameter, SqlGraphPathTestSupport.Graph));
        var wrongString = SqlGraphPathTestSupport.Parameters((GraphParameter, new[] { SqlGraphPathTestSupport.Graph }));
        var wrongInteger = SqlGraphPathTestSupport.Parameters(
            (GraphParameter, SqlGraphPathTestSupport.Graph), ("fromCollection", SqlGraphPathTestSupport.Orders),
            ("fromId", SqlGraphPathTestSupport.StartId), ("toCollection", SqlGraphPathTestSupport.Orders),
            ("toId", SqlGraphPathTestSupport.TargetId), (DepthParameter, "2"),
            ("vertices", SqlGraphPathTestSupport.MaximumVertices),
            ("edges", SqlGraphPathTestSupport.MaximumEdges), ("label", SqlGraphPathTestSupport.Label));

        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, ValidSql, missing), ErrorCode.Validation, ParameterDetail, token);
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, ValidSql, wrongString), ErrorCode.Validation, ParameterDetail, token);
        await SqlGraphPathRejectionFlowAssertions.AssertRejectThenRetryAsync(database, engine,
            SqlGraphPathTestSupport.Request(database, ValidSql, wrongInteger), ErrorCode.Validation, ParameterDetail, token);
    }

    [Test]
    public async Task ParameterCountIsBounded()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = CreateEngine(database);
        var tooMany = Enumerable.Range(0, MaximumParameters).ToDictionary(index => "p" + index,
            index => JsonSerializer.SerializeToElement(index), StringComparer.Ordinal);
        var parameterFailure = Failure(engine, SqlGraphPathTestSupport.Request(database,
            SqlGraphPathTestSupport.LiteralSql, tooMany));

        await Assert.That(parameterFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static QueryEngine CreateEngine(TestDatabase database)
        => new(database.Database, UnitExecutionOptions.QueryExecution());

    private static KeyLoadException Failure(QueryEngine engine, SqlGraphPathRequest request)
        => Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPathSql(SqlGraphPathTestSupport.Principal, request,
            cancellationToken: TestContext.Current!.Execution.CancellationToken));
}
