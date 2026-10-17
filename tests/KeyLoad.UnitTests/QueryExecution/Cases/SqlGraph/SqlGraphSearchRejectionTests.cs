using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphSearchRejectionTests
{
    private const string SearchPrefix = "SELECT * FROM GRAPH_SEARCH('graph-search-documents', ";
    private const string Retriever = "GRAPH_RETRIEVER('graph-search-links', "
        + "GRAPH_SEEDS(ENTITY('graph-search-projects','root-node')),4,20,40,NULL,1)";
    private const string GraphTail = ", NULL, " + Retriever + ", NULL, NULL, 10, 60)";
    private const string GraphOnly = SearchPrefix + "NULL, NULL, NULL, " + Retriever + ", NULL, NULL, 10, 60)";
    private const string DuplicateRetriever = SearchPrefix + "NULL, NULL, NULL, "
        + Retriever + ", " + Retriever + ", NULL, NULL, 10, 60)";
    private const string TextParameter = "text";
    private const string VectorParameter = "vector";

    [Test]
    public async Task InvalidVersionCursorAndFullScanFlagRejectBeforeRead()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());

        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition, GraphOnly) with
        {
            Version = 2
        });
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
            SearchPrefix + "NULL, NULL, " + Retriever + ", NULL, NULL, NULL, 10, 60)"), ErrorCode.UnsupportedCapability);
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition,
            SearchPrefix + "NULL, NULL, NULL, NULL, NULL, NULL, 10, 60)"));
        await AssertRejected(engine, SqlGraphSearchTestSupport.Request(database.Partition,
            SearchPrefix + "NULL, NULL, NULL, GRAPH_UNKNOWN(), NULL, NULL, 10, 60)"), ErrorCode.UnsupportedCapability);
    }

    [Test]
    public async Task WrongParameterKindsAndVectorDimensionsReject()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        const string textSql = SearchPrefix + "TEXT_MATCH('/text',@text,1), NULL" + GraphTail;
        const string vectorSql = SearchPrefix + "NULL, VECTOR_MATCH('/embedding',@vector,"
            + "VECTOR_SPACE('graph-sql-space',2,'Cosine','graph-sql-model','graphsqlversion'),1)" + GraphTail;

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
        const string sql = SearchPrefix + "NULL, NULL, GRAPH_SCOPE('graph-search-links', "
            + "GRAPH_SEEDS(ENTITY('graph-search-projects','root-node')),4,20,40,GRAPH_LABELS()), "
            + Retriever + ", NULL, NULL, 10, 60)";

        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchSqlAsync(
            GraphSearchTestSupport.Reader, SqlGraphSearchTestSupport.Request(database.Partition, sql),
            TestContext.Current!.Execution.CancellationToken)))!;

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    [Test]
    public async Task ParameterCountAndCompleteRequestBoundsAreEnforced()
    {
        using var database = new TestDatabase();
        var engine = new QueryEngine(new KeyLoad.Core.DatabaseEngine(database.Store, new KeyLoad.Core.Features.Authorization.AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxQueryBytes = 512
        }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance), UnitExecutionOptions.QueryExecution());
        var excessiveParameters = Enumerable.Range(0, 257)
            .ToDictionary(index => $"p{index}", index => System.Text.Json.JsonSerializer.SerializeToElement(index));
        const string textSql = SearchPrefix + "TEXT_MATCH('/text',@text,1), NULL" + GraphTail;

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

    private static async Task AssertRejected(QueryEngine engine, SqlGraphSearchRequest request,
        ErrorCode expectedCode = ErrorCode.Validation)
    {
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchSqlAsync("root", request,
            TestContext.Current!.Execution.CancellationToken)))!;
        await Assert.That(failure.Code).IsEqualTo(expectedCode);
    }
}
