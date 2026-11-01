using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class VectorTopKTests
{
    private const string Collection = "ranked";
    private const string Field = "/embedding";
    private const string Root = "root";
    private static readonly VectorSpace Space = new("dot", 1, DistanceMetric.DotProduct, "model", "1");

    [Test]
    [Arguments(1d, "z,y")]
    [Arguments(0d, "a,b")]
    [Arguments(double.Epsilon, "a,b")]
    [Arguments(double.MaxValue, "z,y")]
    public async Task AcSearch001ExactTopKKeepsFusionOrderingEvenWhenWeightsCollapseToZero(double weight, string expected)
    {
        using var db = Create();
        var cut = db.Store.Position;
        var request = new SearchRequest(db.Partition, Collection, VectorField: Field, Vector: [1], Space: Space,
            Limit: 2, VectorWeight: weight);
        var results = await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Root, request);

        await Assert.That(string.Join(',', results.Select(hit => hit.Document.Reference.Id))).IsEqualTo(expected);
        await Assert.That(db.Store.Position).IsEqualTo(cut);
        var filtered = await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Root,
            request with { VectorWeight = 1d, AllowedIds = ["a", "b", "c"] });
        await Assert.That(string.Join(',', filtered.Select(hit => hit.Document.Reference.Id))).IsEqualTo("a,b");
        await Assert.That(filtered[0].Score).IsEqualTo(1d / 61d);
        await Assert.That(filtered[1].Score).IsEqualTo(1d / 62d);
    }

    [Test]
    public async Task AcSearch001TiesRetainOrdinalIdentifiersAndProjectedSourceRevisions()
    {
        using var db = Create();
        db.Commit(new PutVector(Collection, "y", Field, [5], Space, 1));
        var results = await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Root,
            new(db.Partition, Collection, VectorField: Field, Vector: [1], Space: Space, Limit: 2));
        await Assert.That(string.Join(',', results.Select(hit => hit.Document.Reference.Id))).IsEqualTo("y,z");
        await Assert.That(results.All(hit => hit.Document.Revision == 1 && hit.Document.Json == "{}")).IsTrue();
    }

    private static TestDatabase Create()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "z", "{}"), new PutDocument(Collection, "y", "{}"),
            new PutDocument(Collection, "a", "{}"), new PutDocument(Collection, "b", "{}"), new PutDocument(Collection, "c", "{}"),
            new PutVector(Collection, "z", Field, [5], Space, 1), new PutVector(Collection, "y", Field, [4], Space, 1),
            new PutVector(Collection, "a", Field, [3], Space, 1), new PutVector(Collection, "b", Field, [2], Space, 1),
            new PutVector(Collection, "c", Field, [1], Space, 1));
        return db;
    }
}
