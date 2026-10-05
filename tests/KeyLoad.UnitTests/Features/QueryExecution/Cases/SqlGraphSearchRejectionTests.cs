using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphSearchRejectionTests
{
    private const string SearchPrefix = "SEARCH FROM \"graph-search-documents\"";
    private const string GraphTail = " RETRIEVE GRAPH \"graph-search-links\" "
        + "SEEDS ((\"graph-search-projects\",'root-node')) DEPTH 4 VERTICES 20 EDGES 40";
    private const string GraphOnly = SearchPrefix + GraphTail;
    private const string DuplicateRetriever = GraphOnly + " RETRIEVE GRAPH \"graph-search-links\" "
        + "SEEDS ((\"graph-search-projects\",'root-node')) DEPTH 4 VERTICES 20 EDGES 40";
    private const string TextParameter = "text";
    private const string VectorParameter = "vector";

    [Test]
    public async Task InvalidVersionCursorAndFullScanFlagRejectBeforeRead()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());

        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly) with { Version = 2 });
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly, cursor: "cursor"));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly, fullScan: false));
    }

    [Test]
    public async Task DuplicateUnknownReorderedAndMissingGraphOperatorsReject()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());

        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, DuplicateRetriever));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition,
            "SEARCH FROM \"graph-search-documents\" RETRIEVE GRAPH \"graph-search-links\" SEEDS "
            + "((\"graph-search-projects\",'root-node')) DEPTH 4 VERTICES 20 EDGES 40 TEXT text MATCH 'x'"));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, "SEARCH FROM \"graph-search-documents\" LIMIT 10"));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly + " CACHE"));
    }

    [Test]
    public async Task WrongParameterKindsAndVectorDimensionsReject()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        const string textSql = SearchPrefix + " TEXT text MATCH @text " + GraphTail;
        const string vectorSql = SearchPrefix + " VECTOR embedding MATCH @vector "
            + "SPACE (\"graph-sql-space\",2,Cosine,\"graph-sql-model\",\"graphsqlversion\") "
            + GraphTail;

        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, textSql,
            SqlGraphSearchTestSupport.Parameters((TextParameter, new[] { "not", "a string" }))));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, textSql,
            new Dictionary<string, JsonElement> { [TextParameter] = default }));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, vectorSql,
            SqlGraphSearchTestSupport.Parameters((VectorParameter, new[] { 1f }))));
        using var overflowingNumber = JsonDocument.Parse("[1e999,0]");
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, vectorSql,
            new Dictionary<string, JsonElement> { [VectorParameter] = overflowingNumber.RootElement.Clone() }));
    }

    [Test]
    public async Task EmptySqlLabelFilterStillRequiresPersistedLabelFieldUse()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database, protectLabels: true);
        GraphSearchTestSupport.AddPath(database);
        GraphSearchTestSupport.PersistReader(database);
        const string sql = "SEARCH FROM \"graph-search-documents\" SCOPE GRAPH \"graph-search-links\" "
            + "SEEDS ((\"graph-search-projects\",'root-node')) DEPTH 4 VERTICES 20 EDGES 40 LABELS () "
            + "RETRIEVE GRAPH \"graph-search-links\" SEEDS ((\"graph-search-projects\",'root-node')) "
            + "DEPTH 4 VERTICES 20 EDGES 40";

        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchSqlAsync(
            GraphSearchTestSupport.Reader, SqlGraphSearchTestSupport.Request(database.Partition, sql),
            TestContext.Current!.Execution.CancellationToken)))!;

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    [Test]
    public async Task ParameterCountAndCompleteRequestBoundsAreEnforced()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store,             new KeyLoad.Security.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new() { MaxQueryBytes = 512 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource()), UnitExecutionOptions.QueryExecution());
        var excessiveParameters = Enumerable.Range(0, 257)
            .ToDictionary(index => $"p{index}", index => System.Text.Json.JsonSerializer.SerializeToElement(index));
        const string textSql = SearchPrefix + " TEXT text MATCH @text " + GraphTail;

        var parameterFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly, excessiveParameters))))!;
        var sizeFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly + " " + new string(' ', 600)))))!;
        var parameterSizeFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, textSql,
                SqlGraphSearchTestSupport.Parameters((TextParameter, new string('n', 600)))))))!;

        await Assert.That(parameterFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(sizeFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(parameterSizeFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task PreCancellationDoesNotAdmitWorkOrPoisonTheNextSqlSearch()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => engine.SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, SqlGraphSearchTestSupport.RetrieverSql()), cancellation.Token));
        var healthy = await engine.SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, SqlGraphSearchTestSupport.RetrieverSql()),
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(healthy.Hits.Length).IsEqualTo(2);
    }

    private static async Task AssertRejected(QueryEngine engine, SqlGraphSearchRequest request)
    {
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchSqlAsync("root", request,
            TestContext.Current!.Execution.CancellationToken)))!;
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }
}
