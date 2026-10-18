using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphIncomingRowAuthorityAssertions
{
    private const int PageVersion = 1;
    private const int IncomingLimit = 10;
    private const long EdgeRevision = 1;
    private const long RepairedRevision = 2;
    private const string Graph = "cross-partition-graph";
    private const string EdgeId = "private-edge";
    private const string Label = "links";
    private const string EmptyJson = "{}";
    private const string Projection = "eventual-reverse.v1";

    internal static async Task HealthyAsync(DatabaseEngine engine, ZoneTreeStore store,
        EntityRef source, EntityRef target, string reader, bool repairedTarget)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        var actual = engine.ReadIncomingGraphEdges(reader,
            new ReadIncomingGraphEdgesRequestV1(PageVersion, target, Graph, IncomingLimit));
        var expected = new GraphIncomingEdgesPageV1(PageVersion,
            [new(new EdgeRecord(EdgeId, source, target, Label, EmptyJson, EdgeRevision), EdgeRevision)],
            cut, Projection);
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await DocumentAsync(engine, source, reader, RepairedRevision);
        await DocumentAsync(engine, target, reader, repairedTarget ? RepairedRevision : EdgeRevision);
        await Assert.That(CanonicalStoreImage.Bytes(store)).IsEquivalentTo(image);
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    internal static async Task RefusedAsync(DatabaseEngine engine, ZoneTreeStore store,
        EntityRef target, string reader, ErrorCode expected)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ReadIncomingGraphEdges(reader,
            new ReadIncomingGraphEdgesRequestV1(PageVersion, target, Graph, IncomingLimit)));
        await Assert.That(failure.Code).IsEqualTo(expected);
        await Assert.That(CanonicalStoreImage.Bytes(store)).IsEquivalentTo(image);
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    private static async Task DocumentAsync(DatabaseEngine engine, EntityRef vertex, string reader, long revision)
    {
        var expected = new DocumentResult(vertex, revision, EmptyJson, false, []);
        await Assert.That(JsonSerializer.Serialize(engine.GetDocument(reader, vertex), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }
}
