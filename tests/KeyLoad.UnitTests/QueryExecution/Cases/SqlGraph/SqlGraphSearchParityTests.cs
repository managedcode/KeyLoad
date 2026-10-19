using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphSearchParityTests
{
    [Test]
    public async Task GraphOnlySqlRetrievalMatchesDirectZoneTreeSearch()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var walk = GraphSearchTestSupport.Walk(
            GraphSearchTestSupport.Vertex(database, SqlGraphSearchTestSupport.Projects, SqlGraphSearchTestSupport.Root));
        var direct = new GraphSearchRequest(1,
            new(database.Partition, SqlGraphSearchTestSupport.Collection, Limit: SqlGraphSearchTestSupport.Limit,
                FusionConstant: SqlGraphSearchTestSupport.Fusion), Retriever: new(walk));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var expected = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, SqlGraphSearchTestSupport.RetrieverSql()), cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    [Test]
    public async Task MultipleSeedsAcrossIntermediateCollectionsMatchDirectShortestHopRanking()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var seeds = new[]
        {
            GraphSearchTestSupport.Vertex(database, SqlGraphSearchTestSupport.Projects, SqlGraphSearchTestSupport.Root),
            GraphSearchTestSupport.Vertex(database, SqlGraphSearchTestSupport.Projects, GraphSearchTestSupport.Middle)
        };
        var direct = new GraphSearchRequest(1,
            new(database.Partition, SqlGraphSearchTestSupport.Collection, Limit: SqlGraphSearchTestSupport.Limit,
                FusionConstant: SqlGraphSearchTestSupport.Fusion),
            Retriever: new(new(SqlGraphSearchTestSupport.Graph, [.. seeds], SqlGraphSearchTestSupport.Depth,
                SqlGraphSearchTestSupport.Vertices, SqlGraphSearchTestSupport.Edges)));
        const string sql = "SELECT * FROM GRAPH_SEARCH('graph-search-documents', NULL, NULL, NULL, "
            + "GRAPH_RETRIEVER('graph-search-links', GRAPH_SEEDS(ENTITY('graph-search-projects','root-node'), "
            + "ENTITY('graph-search-projects','middle-node')), 4, 20, 40, NULL, 1), NULL, NULL, 10, 60)";
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var expected = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, sql), cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Hits[0].Document.Reference.Id).IsEqualTo(GraphSearchTestSupport.FirstHit);
        await Assert.That(actual.Hits[1].Document.Reference.Id).IsEqualTo(GraphSearchTestSupport.SecondHit);
        await Assert.That(actual.Hits[0].Score).IsEqualTo(1d / (SqlGraphSearchTestSupport.Fusion + 1));
        await Assert.That(actual.Hits[1].Score).IsEqualTo(1d / (SqlGraphSearchTestSupport.Fusion + 2));
    }

    [Test]
    public async Task ScopeAndExpansionWithoutRetrieverMatchDirectSearch()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var walk = GraphSearchTestSupport.Walk(
            GraphSearchTestSupport.Vertex(database, SqlGraphSearchTestSupport.Projects, SqlGraphSearchTestSupport.Root));
        var direct = new GraphSearchRequest(1,
            new(database.Partition, SqlGraphSearchTestSupport.Collection, SqlGraphSearchTestSupport.TextField,
                "needle", Limit: SqlGraphSearchTestSupport.Limit),
            Scope: new(walk), Expansion: new(SqlGraphSearchTestSupport.Graph, SqlGraphSearchTestSupport.Depth,
                SqlGraphSearchTestSupport.Vertices, SqlGraphSearchTestSupport.Edges, [GraphSearchTestSupport.Label]));
        const string sql = "SELECT * FROM GRAPH_SEARCH('graph-search-documents', TEXT_MATCH('/text','needle',1), NULL, "
            + "GRAPH_SCOPE('graph-search-links', GRAPH_SEEDS(ENTITY('graph-search-projects','root-node')),4,20,40,NULL), "
            + "NULL, GRAPH_EXPANSION('graph-search-links',4,20,40,GRAPH_LABELS('related')), NULL, 10, 60)";
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var expected = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, sql), cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    [Test]
    public async Task ZeroWeightGraphRetrieverStillUsesCanonicalGraphPathButContributesNoHit()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var direct = new GraphSearchRequest(1,
            new(database.Partition, SqlGraphSearchTestSupport.Collection),
            Retriever: new(GraphSearchTestSupport.Walk(
                GraphSearchTestSupport.Vertex(database, SqlGraphSearchTestSupport.Projects, SqlGraphSearchTestSupport.Root)), 0));
        var sql = SqlGraphSearchTestSupport.RetrieverSql(weight: "0");
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var expected = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, sql), cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Hits).IsEmpty();
    }

    [Test]
    public async Task OrderedSqlOperatorsAndParametersMatchDirectCombinedSearch()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var space = SqlGraphSearchTestSupport.Space();
        database.Commit(
            new PutVector(SqlGraphSearchTestSupport.Collection, GraphSearchTestSupport.FirstHit,
                SqlGraphSearchTestSupport.VectorField, [1, 0], space, 1),
            new PutVector(SqlGraphSearchTestSupport.Collection, GraphSearchTestSupport.SecondHit,
                SqlGraphSearchTestSupport.VectorField, [0, 1], space, 1));
        var walk = GraphSearchTestSupport.Walk(
            GraphSearchTestSupport.Vertex(database, SqlGraphSearchTestSupport.Projects, SqlGraphSearchTestSupport.Root));
        var direct = new GraphSearchRequest(1,
            new(database.Partition, SqlGraphSearchTestSupport.Collection, SqlGraphSearchTestSupport.TextField,
                "needle", SqlGraphSearchTestSupport.VectorField, [1, 0], space,
                Limit: SqlGraphSearchTestSupport.Limit, FusionConstant: SqlGraphSearchTestSupport.Fusion,
                AllowedIds: [GraphSearchTestSupport.FirstHit]),
            Scope: new(walk with
            {
                Labels = [GraphSearchTestSupport.Label]
            }), Retriever: new(walk, 0.5),
            Expansion: new(SqlGraphSearchTestSupport.Graph, SqlGraphSearchTestSupport.Depth,
                SqlGraphSearchTestSupport.Vertices, SqlGraphSearchTestSupport.Edges,
                System.Collections.Immutable.ImmutableArray<string>.Empty));
        const string sql = "SELECT * FROM GRAPH_SEARCH('graph-search-documents', TEXT_MATCH('/text',@text,1), "
            + "VECTOR_MATCH('/embedding',@vector,VECTOR_SPACE('graph-sql-space',2,'Cosine','graph-sql-model','graphsqlversion'),1), "
            + "GRAPH_SCOPE('graph-search-links',GRAPH_SEEDS(ENTITY('graph-search-projects',@root)),4,20,40,GRAPH_LABELS(@label)), "
            + "GRAPH_RETRIEVER('graph-search-links',GRAPH_SEEDS(ENTITY('graph-search-projects',@root)),4,20,40,NULL,0.5), "
            + "GRAPH_EXPANSION('graph-search-links',4,20,40,GRAPH_LABELS()), GRAPH_IDS(@hit), 10, 60)";
        var parameters = SqlGraphSearchTestSupport.Parameters(
            ("text", "needle"), ("vector", new[] { 1f, 0f }), ("root", SqlGraphSearchTestSupport.Root),
            ("label", GraphSearchTestSupport.Label),
            ("hit", GraphSearchTestSupport.FirstHit));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var expected = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, sql, parameters), cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    [Test]
    public async Task CapabilityManifestAdvertisesSearchProfileWithoutChangingQ1Dialect()
    {
        using var database = new TestDatabase();

        var manifest = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).Capabilities;

        await Assert.That(manifest.SqlDialect).IsEqualTo("TSQL.ScriptDOM180");
        await Assert.That(manifest.AstVersion).IsEqualTo(1);
        await Assert.That(manifest.SupportedAstVersions).IsEquivalentTo([1]);
        await Assert.That(manifest.SupportedQueryDialectVersions).IsEquivalentTo([1]);
        await Assert.That(manifest.ReadProfiles).Contains("graph-search");
        await Assert.That(manifest.ReadProfiles).Contains("Q2.InnerJoin");
    }

    [Test]
    public async Task EmptyAllowlistReturnsNoHitsThroughTheAuthorizedGraphPath()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var direct = new GraphSearchRequest(1,
            new(database.Partition, SqlGraphSearchTestSupport.Collection,
                AllowedIds: System.Collections.Immutable.ImmutableArray<string>.Empty),
            Retriever: new(GraphSearchTestSupport.Walk(
                GraphSearchTestSupport.Vertex(database, SqlGraphSearchTestSupport.Projects, SqlGraphSearchTestSupport.Root))));
        var actual = await new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition,
                SqlGraphSearchTestSupport.RetrieverSql(allowedIds: "")),
            TestContext.Current!.Execution.CancellationToken);
        var expected = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync("root", direct,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Hits).IsEmpty();
    }
}
