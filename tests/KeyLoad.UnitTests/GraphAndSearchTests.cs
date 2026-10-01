using KeyLoad.Query;

namespace KeyLoad.UnitTests;

public sealed class GraphAndSearchTests
{
    [Fact]
    public void TraversalStopsAtHiddenIntermediateVerticesAndHandlesCycles()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("links", ResourceKind.Graph);
        db.Commit(new PutDocument("orders", "a", "{}", Access: new("alice")), new PutDocument("orders", "b", "{}", Access: new("bob")), new PutDocument("orders", "c", "{}", Access: new("alice")));
        EntityRef Vertex(string id) => new(db.Partition, "orders", id);
        db.Commit(new UpsertEdge("links", "ab", Vertex("a"), Vertex("b"), "next"), new UpsertEdge("links", "bc", Vertex("b"), Vertex("c"), "next"), new UpsertEdge("links", "ca", Vertex("c"), Vertex("a"), "next"));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("alice", "tenant",
            [new("database", "orders", Capability.DocumentsRead), new("database", "links", Capability.GraphRead)], []) { OwnerId = "alice", RestrictRows = true })).Get<PrincipalRecord>();
        var hidden = db.Database.Traverse("alice", db.Partition, "links", Vertex("a"));
        Assert.Single(hidden.Vertices); Assert.Empty(hidden.Edges);
        Assert.Equal(3, db.Database.Traverse("root", db.Partition, "links", Vertex("a"), maxDepth: 10).Vertices.Length);
    }
    [Fact]
    public void OutOfOrderSamplesStayOrderedAndSampleIdIsIdempotent()
    {
        using var db = new TestDatabase(); db.Configure("metrics", ResourceKind.TimeSeries);
        var now = DateTimeOffset.UtcNow;
        db.Commit(new AppendSamples("metrics", "cpu", [new("later", now, 2), new("earlier", now.AddSeconds(-1), 1)]));
        db.Commit(new AppendSamples("metrics", "cpu", [new("later", now, 2)]));
        Assert.Equal(new[] { "earlier", "later" }, db.Database.ReadSamples("root", db.Partition, "metrics", "cpu", now.AddMinutes(-1), now.AddMinutes(1)).Select(r => r.Sample.EventId));
    }
    [Fact]
    public void HybridFusionRanksEligibleDocumentsAndInvalidatesStaleVectors()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{\"text\":\"cluster database\"}"), new PutDocument("orders", "b", "{\"text\":\"database\"}"));
        var space = new VectorSpace("test", 2, DistanceMetric.Cosine, "test", "1");
        db.Commit(new PutVector("orders", "a", "/embedding", [1, 0], space, 1), new PutVector("orders", "b", "/embedding", [0, 1], space, 1));
        var search = new SearchEngine(db.Database);
        var result = search.Search("root", new(db.Partition, "orders", "/text", "cluster", "/embedding", [1, 0], space));
        Assert.Equal("a", result[0].Document.Reference.Id);
        Assert.Equal(2.0 / 61, result[0].Score, 12);
        db.Commit(new PatchDocument("orders", "a", [new("/text", PatchKind.Set, "\"changed\"")], 1));
        var vectors = search.Search("root", new(db.Partition, "orders", VectorField: "/embedding", Vector: [1, 0], Space: space));
        Assert.Equal("b", Assert.Single(vectors).Document.Reference.Id);
    }
    [Fact]
    public void LargeFiniteVectorsAndSampleValuesRemainValidAndSimilarityDoesNotOverflow()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection); db.Configure("metrics", ResourceKind.TimeSeries);
        var values = Enumerable.Repeat(float.MaxValue, System.Numerics.Vector<float>.Count + 1).ToArray();
        var space = new VectorSpace("large", values.Length, DistanceMetric.Cosine, "test", "1");
        db.Commit(new PutDocument("orders", "large", "{}"), new PutVector("orders", "large", "/embedding", values, space, 1),
            new AppendSamples("metrics", "large", [new("sample", DateTimeOffset.UtcNow, 1e300)]));
        var match = Assert.Single(new SearchEngine(db.Database).Search("root", new(db.Partition, "orders",
            VectorField: "/embedding", Vector: values, Space: space)));
        Assert.Equal("large", match.Document.Reference.Id);
        Assert.Equal(1, SearchEngine.Similarity(values, values, DistanceMetric.Cosine), 12);
        Assert.True(double.IsFinite(SearchEngine.Similarity(values, values, DistanceMetric.DotProduct)));
        Assert.Equal(0, SearchEngine.Similarity(values, values, DistanceMetric.Euclidean));
    }
}
