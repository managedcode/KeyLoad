using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphStorageReferenceAssertions
{
    internal static async Task VerifyAsync(DatabaseEngine owner, PartitionRef partition,
        IReadOnlyDictionary<string, GraphStorageExpectedEdge> expected, IEnumerable<string>? additionalVertices = null)
    {
        var canonical = owner.Store.Read(view =>
        {
            var records = new List<EdgeRecord>();
            var scan = view.VisitRange(KeySpace.Partition("edge", partition, GraphStorageReferenceFixture.Graph), 100,
                (_, bytes) => { records.Add(NativeSerialization.Deserialize<EdgeRecord>(bytes)!); return true; });
            if (scan.HasMore)
            { throw new InvalidOperationException("The graph oracle range is incomplete."); }
            return records;
        });
        await CompareAsync(canonical, expected.Values, partition);
        await VerifyAdjacencyCountAsync(owner, partition, "out", expected.Count);
        await VerifyAdjacencyCountAsync(owner, partition, "in", expected.Count);
        var vertices = Enumerable.Range(0, GraphStorageReferenceFixture.VertexCount)
            .Select(GraphStorageReferenceFixture.VertexId).Concat(additionalVertices ?? []);
        foreach (var id in vertices)
        {
            var vertex = GraphStorageReferenceFixture.Vertex(partition, id);
            var outgoing = owner.Traverse(GraphStorageReferenceFixture.Root, partition,
                GraphStorageReferenceFixture.Graph, vertex, maxDepth: 1);
            await CompareAsync(outgoing.Edges, expected.Values.Where(edge => edge.From == id), partition);
            var incoming = owner.ReadIncomingGraphEdges(GraphStorageReferenceFixture.Root,
                new ReadIncomingGraphEdgesRequestV1(1, vertex, GraphStorageReferenceFixture.Graph, 100));
            await CompareAsync(incoming.Rows.Select(row => row.Edge), expected.Values.Where(edge => edge.To == id), partition);
            await Assert.That(incoming.Rows.All(row => row.DeliveredRevision == 0)).IsTrue();
        }
    }

    private static async Task VerifyAdjacencyCountAsync(DatabaseEngine owner, PartitionRef partition, string direction, int expected)
    {
        var count = owner.Store.Read(view =>
        {
            var found = 0;
            var scan = view.VisitRange(KeySpace.Partition("adjacency", partition, GraphStorageReferenceFixture.Graph, direction), 100,
                (_, _) => { found++; return true; });
            if (scan.HasMore)
            { throw new InvalidOperationException("The graph adjacency oracle range is incomplete."); }
            return found;
        });
        await Assert.That(count).IsEqualTo(expected);
    }

    private static async Task CompareAsync(IEnumerable<EdgeRecord> actual, IEnumerable<GraphStorageExpectedEdge> expected,
        PartitionRef partition)
    {
        var edges = actual.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
        var reference = expected.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
        await Assert.That(edges.Length).IsEqualTo(reference.Length);
        for (var index = 0; index < reference.Length; index++)
        {
            var edge = edges[index];
            var literal = reference[index];
            await Assert.That(edge.Id).IsEqualTo(literal.Id);
            await Assert.That(edge.From).IsEqualTo(GraphStorageReferenceFixture.Vertex(partition, literal.From));
            await Assert.That(edge.To).IsEqualTo(GraphStorageReferenceFixture.Vertex(partition, literal.To));
            await Assert.That(edge.Label).IsEqualTo(literal.Label);
            await Assert.That(edge.AttributesJson).IsEqualTo(literal.Json);
            await Assert.That(edge.Revision).IsEqualTo(literal.Revision);
        }
    }
}
