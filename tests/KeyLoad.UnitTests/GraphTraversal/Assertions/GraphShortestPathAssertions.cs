using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphShortestPathAssertions
{
    internal static async Task<GraphShortestPathResult[]> AssertPrimaryPathsAsync(TestDatabase database,
        GraphShortestPathReferenceEdge[] edges, GraphShortestPathReferenceVertex start,
        GraphShortestPathReferenceVertex first, GraphShortestPathReferenceVertex join,
        string firstEdgeId, string firstJoinEdgeId,
        string directJoinEdgeId, CancellationToken token)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal) { GraphShortestPathTestSupport.Walk };
        var expectedTie = GraphShortestPathReferenceBfs.Find(edges, start, join, 2, labels: labels);
        var tieRequest = GraphShortestPathTestSupport.Request(database, start, join, maxDepth: 2,
            labels: ImmutableArray.Create(GraphShortestPathTestSupport.Walk));
        var tie = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, tieRequest,
            cancellationToken: token);
        await MatchAsync(database, tie, expectedTie);
        await Assert.That(tie.Edges.Select(edge => edge.Id).SequenceEqual([firstEdgeId, firstJoinEdgeId])).IsTrue();
        await Assert.That(tie.Vertices[1].Collection).IsEqualTo(first.Collection);
        await Assert.That(tie.Vertices[2].Collection).IsEqualTo(join.Collection);

        var expectedDirect = GraphShortestPathReferenceBfs.Find(edges, start, join, 16);
        var directRequest = GraphShortestPathTestSupport.Request(database, start, join);
        var directPath = database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, directRequest,
            cancellationToken: token);
        await MatchAsync(database, directPath, expectedDirect);
        await Assert.That(directPath.Edges.Select(edge => edge.Id).SequenceEqual([directJoinEdgeId])).IsTrue();
        return [tie, directPath];
    }

    internal static async Task<GraphShortestPathResult[]> AssertBoundedPathsAsync(TestDatabase database,
        GraphShortestPathReferenceEdge[] edges, GraphShortestPathReferenceVertex start,
        GraphShortestPathReferenceVertex join, GraphShortestPathReferenceVertex isolated, CancellationToken token)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal) { GraphShortestPathTestSupport.Walk };
        var walkLabels = ImmutableArray.Create(GraphShortestPathTestSupport.Walk);
        var shallow = Search(database, start, join, 1, walkLabels, token);
        var shallowExpected = GraphShortestPathReferenceBfs.Find(edges, start, join, 1, labels: labels);
        await MatchAsync(database, shallow, shallowExpected);

        var zeroDepth = Search(database, start, join, 0, walkLabels, token);
        var zeroDepthExpected = GraphShortestPathReferenceBfs.Find(edges, start, join, 0, labels: labels);
        await MatchAsync(database, zeroDepth, zeroDepthExpected);

        var noPath = Search(database, start, isolated, 16, null, token);
        var noPathExpected = GraphShortestPathReferenceBfs.Find(edges, start, isolated, 16);
        await MatchAsync(database, noPath, noPathExpected);

        var zeroHop = Search(database, start, start, 0, null, token);
        await MatchAsync(database, zeroHop, GraphShortestPathReferenceBfs.Find(edges, start, start, 0));
        await Assert.That(zeroHop.Found).IsTrue();
        await Assert.That(zeroHop.Hops).IsEqualTo(0);
        await Assert.That(zeroHop.Vertices.SequenceEqual([GraphShortestPathTestSupport.Vertex(database,
            start.Collection, start.Id)])).IsTrue();
        await Assert.That(zeroHop.Edges).IsEmpty();
        return [shallow, zeroDepth, noPath, zeroHop];
    }

    internal static async Task AssertSameCutAsync(TestDatabase database, GraphShortestPathResult[] results)
    {
        await Assert.That(results.Select(result => result.CutPosition).All(position =>
            position == database.Store.Position)).IsTrue();
    }

    private static GraphShortestPathResult Search(TestDatabase database,
        GraphShortestPathReferenceVertex start, GraphShortestPathReferenceVertex target,
        int maxDepth, ImmutableArray<string>? labels, CancellationToken token)
    {
        var request = GraphShortestPathTestSupport.Request(database, start, target, maxDepth, labels: labels);
        return database.Database.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, request,
            cancellationToken: token);
    }

    internal static async Task MatchAsync(TestDatabase database, GraphShortestPathResult actual,
        GraphShortestPathReference expected)
    {
        await Assert.That(actual.Version).IsEqualTo(1);
        await Assert.That(actual.Found).IsEqualTo(expected.Found);
        await Assert.That(actual.Hops).IsEqualTo(expected.Found ? expected.Edges.Length : null);
        var expectedVertices = expected.Vertices.Select(vertex =>
            GraphShortestPathTestSupport.Vertex(database, vertex.Collection, vertex.Id));
        await Assert.That(actual.Vertices.SequenceEqual(expectedVertices)).IsTrue();
        await Assert.That(actual.Edges.Select(edge => edge.Id)
            .SequenceEqual(expected.Edges.Select(edge => edge.Id))).IsTrue();
        var expectedFrom = expected.Edges.Select(edge =>
            GraphShortestPathTestSupport.Vertex(database, edge.From.Collection, edge.From.Id));
        var expectedTo = expected.Edges.Select(edge =>
            GraphShortestPathTestSupport.Vertex(database, edge.To.Collection, edge.To.Id));
        await Assert.That(actual.Edges.Select(edge => edge.From).SequenceEqual(expectedFrom)).IsTrue();
        await Assert.That(actual.Edges.Select(edge => edge.To).SequenceEqual(expectedTo)).IsTrue();
        await Assert.That(actual.Edges.Select(edge => edge.Label)
            .SequenceEqual(expected.Edges.Select(edge => edge.Label))).IsTrue();
        await Assert.That(actual.Edges.Select(edge => edge.AttributesJson)
            .SequenceEqual(expected.Edges.Select(edge => edge.AttributesJson))).IsTrue();
    }
}
