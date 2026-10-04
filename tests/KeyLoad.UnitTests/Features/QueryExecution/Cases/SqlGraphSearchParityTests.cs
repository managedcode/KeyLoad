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

        var expected = await new SearchEngine(database.Database).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database).SearchSqlAsync("root",
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
        const string sql = "SEARCH FROM \"graph-search-documents\" RETRIEVE GRAPH \"graph-search-links\" "
            + "SEEDS ((\"graph-search-projects\",'root-node'),(\"graph-search-projects\",'middle-node')) "
            + "DEPTH 4 VERTICES 20 EDGES 40 LIMIT 10 FUSION 60";
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var expected = await new SearchEngine(database.Database).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, sql), cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Hits[0].Score).IsEqualTo(actual.Hits[1].Score);
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
        const string sql = "SEARCH FROM \"graph-search-documents\" TEXT text MATCH 'needle' "
            + "SCOPE GRAPH \"graph-search-links\" SEEDS ((\"graph-search-projects\",'root-node')) "
            + "DEPTH 4 VERTICES 20 EDGES 40 EXPAND GRAPH \"graph-search-links\" DEPTH 4 "
            + "VERTICES 20 EDGES 40 LABELS ('related') LIMIT 10 FUSION 60";
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var expected = await new SearchEngine(database.Database).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database).SearchSqlAsync("root",
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

        var expected = await new SearchEngine(database.Database).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database).SearchSqlAsync("root",
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
            Scope: new(walk with { Labels = [GraphSearchTestSupport.Label] }), Retriever: new(walk, 0.5),
            Expansion: new(SqlGraphSearchTestSupport.Graph, SqlGraphSearchTestSupport.Depth,
                SqlGraphSearchTestSupport.Vertices, SqlGraphSearchTestSupport.Edges,
                System.Collections.Immutable.ImmutableArray<string>.Empty));
        const string sql = "SEARCH FROM \"graph-search-documents\" TEXT text MATCH @text WEIGHT 1 "
            + "VECTOR embedding MATCH @vector SPACE (\"graph-sql-space\",2,Cosine,\"graph-sql-model\",\"graphsqlversion\") WEIGHT 1 "
            + "SCOPE GRAPH \"graph-search-links\" SEEDS ((\"graph-search-projects\",@root)) DEPTH 4 VERTICES 20 EDGES 40 LABELS (@label) "
            + "RETRIEVE GRAPH \"graph-search-links\" SEEDS ((\"graph-search-projects\",@root)) DEPTH 4 VERTICES 20 EDGES 40 WEIGHT 0.5 "
            + "EXPAND GRAPH \"graph-search-links\" DEPTH 4 VERTICES 20 EDGES 40 LABELS () "
            + "ALLOW IDS (@hit) LIMIT 10 FUSION 60";
        var parameters = SqlGraphSearchTestSupport.Parameters(
            ("text", "needle"), ("vector", new[] { 1f, 0f }), ("root", SqlGraphSearchTestSupport.Root),
            ("label", GraphSearchTestSupport.Label),
            ("hit", GraphSearchTestSupport.FirstHit));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var expected = await new SearchEngine(database.Database).GraphSearchAsync("root", direct, cancellationToken);
        var actual = await new QueryEngine(database.Database).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition, sql, parameters), cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    [Test]
    public async Task CapabilityManifestAdvertisesSearchProfileWithoutChangingQ1Dialect()
    {
        using var database = new TestDatabase();

        var manifest = new QueryEngine(database.Database).Capabilities;

        await Assert.That(manifest.SqlDialect).IsEqualTo("Q1");
        await Assert.That(manifest.ReadProfiles).Contains("graph-search-v1");
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
        var actual = await new QueryEngine(database.Database).SearchSqlAsync("root",
            SqlGraphSearchTestSupport.Request(database.Partition,
                SqlGraphSearchTestSupport.RetrieverSql(allowedIds: "")),
            TestContext.Current!.Execution.CancellationToken);
        var expected = await new SearchEngine(database.Database).GraphSearchAsync("root", direct,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Hits).IsEmpty();
    }
}
