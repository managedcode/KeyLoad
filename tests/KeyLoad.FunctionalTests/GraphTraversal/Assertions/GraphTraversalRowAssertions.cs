using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal static class GraphTraversalRowAssertions
{
    private const string Reader = "alice";
    private const string Collection = "orders";
    private const string Graph = "links";
    private const string A = "a";
    private const string B = "b";
    private const string C = "c";
    private const string Label = "next";
    private const string EmptyJson = "{}";
    private const long EdgeRevision = 1;

    internal static async Task ReadAsync(DatabaseEngine engine, ZoneTreeStore store, TestDatabase source,
        bool visible, CancellationToken token)
    {
        var partition = source.Partition;
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        EntityRef Ref(string id) => source.ExpectedReference(new(partition, Collection, id));
        var actual = engine.Traverse(Reader, partition, Graph, Ref(A), cancellationToken: token);
        EntityRef[] vertices = visible ? [Ref(A), Ref(B), Ref(C)] : [Ref(A)];
        EdgeRecord[] edges = visible ? [new("ab", Ref(A), Ref(B), Label, EmptyJson, EdgeRevision),
            new("bc", Ref(B), Ref(C), Label, EmptyJson, EdgeRevision),
            new("ca", Ref(C), Ref(A), Label, EmptyJson, EdgeRevision)] : [];
        await Assert.That(JsonDefaults.Serialize(actual.Vertices).AsSpan().SequenceEqual(JsonDefaults.Serialize(vertices))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.Edges).AsSpan().SequenceEqual(JsonDefaults.Serialize(edges))).IsTrue();
        await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    internal static async Task RefusedAsync(DatabaseEngine engine, ZoneTreeStore store, PartitionRef partition,
        ErrorCode code, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Traverse(Reader, partition, Graph,
            new(partition, Collection, A), cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(cut);
    }
}
