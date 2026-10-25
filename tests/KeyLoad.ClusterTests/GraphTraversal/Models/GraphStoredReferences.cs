namespace KeyLoad.ClusterTests.GraphTraversal;

/// <summary>Builds expected graph values with the resource lifetimes the server binds to every stored vertex.</summary>
internal static class GraphStoredReferences
{
    internal static EdgeRecord Edge(EdgeRecord edge, Func<EntityRef, EntityRef> stored)
        => edge with
        {
            From = stored(edge.From),
            To = stored(edge.To)
        };

    internal static global::KeyLoad.GraphTraversal Traversal(global::KeyLoad.GraphTraversal traversal,
        Func<EntityRef, EntityRef> stored)
        => new([.. traversal.Vertices.Select(stored)], [.. traversal.Edges.Select(edge => Edge(edge, stored))]);
}
