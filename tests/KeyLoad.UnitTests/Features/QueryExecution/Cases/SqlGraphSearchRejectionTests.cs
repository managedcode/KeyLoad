using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphSearchRejectionTests
{
    private const string GraphOnly = "SEARCH FROM \"graph-search-documents\" RETRIEVE GRAPH \"graph-search-links\" "
        + "SEEDS ((\"graph-search-projects\",'root-node')) DEPTH 4 VERTICES 20 EDGES 40";
    private const string DuplicateRetriever = GraphOnly + " RETRIEVE GRAPH \"graph-search-links\" "
        + "SEEDS ((\"graph-search-projects\",'root-node')) DEPTH 4 VERTICES 20 EDGES 40";

    [Test]
    public async Task InvalidVersionCursorAndFullScanFlagRejectBeforeRead()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(database.Database);

        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly) with { Version = 2 });
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly, cursor: "cursor"));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly, fullScan: false));
    }

    [Test]
    public async Task DuplicateUnknownReorderedAndMissingGraphOperatorsReject()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(database.Database);

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
        var engine = new QueryEngine(database.Database);
        const string textSql = "SEARCH FROM \"graph-search-documents\" TEXT text MATCH @text "
            + GraphOnly["SEARCH FROM \"graph-search-documents\"".Length..];
        const string vectorSql = "SEARCH FROM \"graph-search-documents\" VECTOR embedding MATCH @vector "
            + "SPACE (\"graph-sql-space\",2,Cosine,\"graph-sql-model\",1) "
            + GraphOnly["SEARCH FROM \"graph-search-documents\"".Length..];

        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, textSql,
            SqlGraphSearchTestSupport.Parameters(("text", new[] { "not", "a string" }))));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, textSql,
            new Dictionary<string, JsonElement> { ["text"] = default }));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, vectorSql,
            SqlGraphSearchTestSupport.Parameters(("vector", new[] { 1f }))));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, vectorSql,
            SqlGraphSearchTestSupport.Parameters(("vector", new[] { float.PositiveInfinity, 0f }))));
    }

    [Test]
    public async Task ParameterCountAndCompleteRequestBoundsAreEnforced()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store,
            new KeyLoad.Security.AuthorizationPolicy(), new() { MaxQueryBytes = 512 }));
        var excessiveParameters = Enumerable.Range(0, 257)
            .ToDictionary(index => $"p{index}", index => System.Text.Json.JsonSerializer.SerializeToElement(index));

        var parameterFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly, excessiveParameters))))!;
        var sizeFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly + " " + new string(' ', 600)))))!;

        await Assert.That(parameterFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(sizeFailure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task PreCancellationDoesNotAdmitWorkOrPoisonTheNextSqlSearch()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var engine = new QueryEngine(database.Database);
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
