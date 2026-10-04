using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class ImmutableVectorInputTests
{
    private const string Collection = "orders";
    private const string Principal = "root";
    private const string VectorField = "/embedding";
    private const string TextField = "/text";
    private const string Hit = "hit";
    private const string Rival = "a-rival";
    private const string QueryTerm = "needle";
    private static VectorSpace Space { get; } = new("vector-input", 2, DistanceMetric.DotProduct, "model", "1");

    [Test]
    public async Task AcRoc003_DefaultQueryVectorRejectsAndOwnedVectorInputKeepsItsRank()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, Hit, "{\"text\":\"needle\"}"),
            new PutDocument(Collection, Rival, "{}"));
        var source = new float[] { 1, 0 };
        var stored = ImmutableArray.CreateRange(source);
        db.Commit(new PutVector(Collection, Hit, VectorField, stored, Space, 1));
        db.Commit(new PutVector(Collection, Rival, VectorField, [0.5f, 0], Space, 1));
        source[0] = 0;
        var search = new SearchEngine(db.Database);
        var invalid = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => search.SearchAsync(Principal,
            new(db.Partition, Collection, VectorField: VectorField,
                Vector: default(ImmutableArray<float>), Space: Space))))!;
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);

        var querySource = new float[] { 1, 0 };
        var query = ImmutableArray.CreateRange(querySource);
        querySource[0] = 0;
        var ranked = await Assert.That(await search.SearchAsync(Principal,
            new(db.Partition, Collection, VectorField: VectorField, Vector: query, Space: Space, Limit: 1))).HasSingleItem();
        await Assert.That(ranked.Document.Reference.Id).IsEqualTo(Hit);
        await Assert.That(ranked.Score).IsEqualTo(1.0 / 61);
        await Assert.That(await search.SearchAsync(Principal,
            new(db.Partition, Collection, TextField, QueryTerm))).HasSingleItem();
    }
}
