using System.Numerics;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

/// <summary>persisted exact top-k across all metrics, cancellation and healthy reuse.</summary>
internal sealed class CanonicalVectorPublicMetricTests
{
    private const string Collection = "public-metric-oracle";
    private const string Field = "/embedding";
    private const string Root = "root";

    [Test]
    [Arguments(DistanceMetric.DotProduct)]
    [Arguments(DistanceMetric.Cosine)]
    [Arguments(DistanceMetric.Euclidean)]
    public async Task ExactTopKMatchesLiteralMetricOrderAndCanceledReadPreservesEveryDocument(
        DistanceMetric metric)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var query = Coordinates(1, 0);
        var space = new VectorSpace("literal-space", query.Length, metric, "literal-model", "1");
        database.Commit(new PutDocument(Collection, "a", "{\"label\":\"a\"}"),
            new PutDocument(Collection, "b", "{\"label\":\"b\"}"),
            new PutDocument(Collection, "c", "{\"label\":\"c\"}"),
            new PutVector(Collection, "a", Field, Coordinates(3, BitConverter.Int32BitsToSingle(int.MinValue)), space, 1),
            new PutVector(Collection, "b", Field, Coordinates(1, 1), space, 1),
            new PutVector(Collection, "c", Field, Coordinates(0, 3), space, 1));
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(database.Partition, Collection, VectorField: Field,
            Vector: query, Space: space, Limit: 2);
        await VerifyPackedFrameAsync(database, search, request);
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
        {
            _ = await search.SearchAsync(Root, request, canceled.Token);
        }
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
        var healthy = await search.SearchAsync(Root, request with
        {
            Limit = 3
        },
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(healthy.Select(row => row.Document.Reference.Id)).IsEquivalentTo(
            expected.Concat(["c"]), CollectionOrdering.Matching);
        await Assert.That(healthy.Select(row => row.Score)).IsEquivalentTo(
            new[] { 1d / 61, 1d / 62, 1d / 63 }, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    private static async Task VerifyPackedFrameAsync(TestDatabase database, SearchEngine search, SearchRequest request)
    {
        byte[] prefix = [0, 0, 64, 64, 0, 0, 0, 128];
        byte[] extremeBits = [255, 255, 127, 127, 255, 255, 127, 255];
        var key = KeySpace.Partition("vector", database.Partition, Collection, Field, "a");
        var original = database.Store.Read(view => view.ReadOwnedValue(key))!;
        var vector = OrleansSerialization.Deserialize<VectorStorageRecord>(original);
        await Assert.That(vector.PackedCoordinates.Length).IsEqualTo(request.Space!.Dimension * sizeof(float));
        await Assert.That(vector.PackedCoordinates.AsSpan().StartsWith(prefix)).IsTrue();
        await Assert.That(VectorStorageRecord.Pack([float.MaxValue, float.MinValue])).IsEquivalentTo(extremeBits, CollectionOrdering.Matching);

        var malformed = OrleansSerialization.Serialize(vector with
        {
            PackedCoordinates = [0, 0, 64]
        });
        Write(malformed);
        var position = database.Store.Position;
        try
        {
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => search.SearchAsync(Root, request,
                TestContext.Current!.Execution.CancellationToken)) ?? throw new InvalidOperationException("Expected a corrupt vector error.");
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await Assert.That(database.Store.Read(view => view.ReadOwnedValue(key))).IsEquivalentTo(malformed, CollectionOrdering.Matching);
        }
        finally
        {
            Write(original);
        }

        void Write(byte[] bytes)
        {
            database.Store.Commit((transaction, _) =>
            {
                transaction.Put(key, bytes);
                return true;
            });
        }
    }

    private static global::System.Collections.Immutable.ImmutableArray<float> Coordinates(float first, float second)
    {
        var values = new float[Vector<float>.Count + 1];
        values[0] = first;
        values[1] = second;
        return [.. values];
    }
}
