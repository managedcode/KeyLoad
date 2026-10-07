using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>KL027: persisted exact top-k across all metrics, cancellation and healthy reuse.</summary>
internal sealed class CanonicalVectorPublicMetricTests
{
    private const string Collection = "public-metric-oracle";
    private const string Field = "/embedding";
    private const string Root = "root";

    [Test]
    [Arguments(DistanceMetric.DotProduct)]
    [Arguments(DistanceMetric.Cosine)]
    [Arguments(DistanceMetric.Euclidean)]
    public async Task Kl027NativeExactTopKMatchesLiteralMetricOrderAndCanceledReadPreservesEveryDocument(
        DistanceMetric metric)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var space = new VectorSpace("literal-space", 2, metric, "literal-model", "1");
        database.Commit(new PutDocument(Collection, "a", "{\"label\":\"a\"}"),
            new PutDocument(Collection, "b", "{\"label\":\"b\"}"),
            new PutDocument(Collection, "c", "{\"label\":\"c\"}"),
            new PutVector(Collection, "a", Field, [3, 0], space, 1),
            new PutVector(Collection, "b", Field, [1, 1], space, 1),
            new PutVector(Collection, "c", Field, [0, 3], space, 1));
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(database.Partition, Collection, VectorField: Field,
            Vector: [1, 0], Space: space, Limit: 2);
        // Literal independent geometry: dot3,1,0; cosine1,1/sqrt2,0;
        // Euclidean distances2,1,sqrt10. Fusion uses ranks, not these raw distances.
        var expected = metric == DistanceMetric.Euclidean ? new[] { "b", "a" } : new[] { "a", "b" };
        var position = database.Store.Position;
        var original = new[] { "a", "b", "c" }.Select(id => database.Database.GetDocument(Root,
            new(database.Partition, Collection, id))!).ToArray();
        var initial = await search.SearchAsync(Root, request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(initial.Select(row => row.Document.Reference.Id))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(initial.Select(row => row.Score)).IsEquivalentTo(
            new[] { 1d / 61, 1d / 62 }, CollectionOrdering.Matching);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        OperationCanceledException? failure = null;
        try
        { _ = await search.SearchAsync(Root, request, canceled.Token); }
        catch (OperationCanceledException exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.CancellationToken).IsEqualTo(canceled.Token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        foreach (var document in original)
        {
            var current = database.Database.GetDocument(Root, document.Reference)!;
            await Assert.That(current.Reference).IsEqualTo(document.Reference);
            await Assert.That(current.Json).IsEqualTo(document.Json);
            await Assert.That(current.Json).IsEqualTo("{\"label\":\"" + document.Reference.Id + "\"}");
            await Assert.That(current.Revision).IsEqualTo(1L);
        }
        var healthy = await search.SearchAsync(Root, request with { Limit = 3 },
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(healthy.Select(row => row.Document.Reference.Id)).IsEquivalentTo(
            expected.Concat(["c"]), CollectionOrdering.Matching);
        await Assert.That(healthy.Select(row => row.Score)).IsEquivalentTo(
            new[] { 1d / 61, 1d / 62, 1d / 63 }, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }
}
