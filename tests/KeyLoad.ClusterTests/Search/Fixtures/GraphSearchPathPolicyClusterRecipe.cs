using System.Collections.Immutable;
using P = KeyLoad.ClusterTests.Search.GraphSearchPathPolicyClusterProtocol;

namespace KeyLoad.ClusterTests.Search;

internal static class GraphSearchPathPolicyClusterRecipe
{
    internal static EntityRef Vertex(PartitionRef partition, string collection, string id) => new(partition, collection, id);
    internal static CommandRequest Seed(PartitionRef partition) => new(Guid.NewGuid(), partition,
        [new PutDocument(P.Projects, P.Root, P.RootJson, Access: new(P.Owner)),
         new PutDocument(P.Projects, P.Middle, P.MiddleJson, Access: new(P.ForeignOwner)),
         new PutDocument(P.Projects, P.Private, P.PrivateJson, Access: new(P.ForeignOwner)),
         new PutDocument(P.Documents, P.Direct, P.DirectJson, Access: new(P.Owner)),
         new PutDocument(P.Documents, P.Target, P.TargetJson, Access: new(P.Owner)),
         new UpsertEdge(P.Graph, P.RootDirect, Vertex(partition, P.Projects, P.Root), Vertex(partition, P.Documents, P.Direct), P.Label),
         new UpsertEdge(P.Graph, P.RootMiddle, Vertex(partition, P.Projects, P.Root), Vertex(partition, P.Projects, P.Middle), P.Label),
         new UpsertEdge(P.Graph, P.MiddleTarget, Vertex(partition, P.Projects, P.Middle), Vertex(partition, P.Documents, P.Target), P.Label)]);

    internal static ImmutableArray<MutationReceipt> SeedReceipts =>
        [new("putDocument", P.Projects, P.Root, 1L), new("putDocument", P.Projects, P.Middle, 1L),
         new("putDocument", P.Projects, P.Private, 1L), new("putDocument", P.Documents, P.Direct, 1L),
         new("putDocument", P.Documents, P.Target, 1L), new("upsertEdge", P.Graph, P.RootDirect, 1L),
         new("upsertEdge", P.Graph, P.RootMiddle, 1L), new("upsertEdge", P.Graph, P.MiddleTarget, 1L)];

    internal static GraphWalkSpec Walk(PartitionRef partition, string id = P.Root)
        => new(P.Graph, [Vertex(partition, P.Projects, id)], P.Depth, P.Vertices, P.Edges);

    internal static (GraphSearchRequest Request, GraphSearchResult Expected)[] Reads(GraphSearchPathPolicyClusterState state, bool visible)
    {
        var partition = state.Partition;
        var walk = Walk(partition);
        var search = new SearchRequest(partition, P.Documents, Limit: P.Results, FusionConstant: P.Fusion);
        var direct = new RankedDocument(Document(state, P.Documents, P.Direct, P.DirectJson), 1d / 61d);
        var target = new RankedDocument(Document(state, P.Documents, P.Target, P.TargetJson), 1d / 62d);
        var scopeSearch = search with
        {
            AllowedIds = [P.Target]
        };
        var rootSearch = search with
        {
            Collection = P.Projects
        };
        var context = new GraphExpansionResult(visible
            ? [new(direct.Document, 1), new(Document(state, P.Projects, P.Middle, P.MiddleJson, state.MiddleRevision), 1),
               new(target.Document, 2)] : [new(direct.Document, 1)], GraphExpansionCompleteness.SelectedHits);
        return
        [(new(P.Version, search, Retriever: new(walk)), new(visible ? [direct, target] : [direct], null)),
         (new(P.Version, scopeSearch, Scope: new(walk), Retriever: new(walk)),
             new(visible ? [target with { Score = 1d / 61d }] : [], null)),
         (new(P.Version, rootSearch, Retriever: new(walk with { MaxDepth = 0 }),
             Expansion: new(P.Graph, P.Depth, P.Vertices, P.Edges)),
             new([new(Document(state, P.Projects, P.Root, P.RootJson), 1d / 61d)], context))];
    }

    internal static GraphSearchRequest HiddenSeed(PartitionRef partition, string id)
        => new(P.Version, new(partition, P.Documents, Limit: P.Results, FusionConstant: P.Fusion), Retriever: new(Walk(partition, id)));

    internal static DocumentResult Document(GraphSearchPathPolicyClusterState state, string collection, string id, string json,
        long revision = 1L)
        => new(state.StoredVertex(collection, id), revision, json, false, []);

    internal static global::KeyLoad.GraphTraversal Graph(GraphSearchPathPolicyClusterState state) => new(
        [state.StoredVertex(P.Documents, P.Direct), state.StoredVertex(P.Documents, P.Target),
         state.StoredVertex(P.Projects, P.Middle), state.StoredVertex(P.Projects, P.Root)],
        [new(P.MiddleTarget, state.StoredVertex(P.Projects, P.Middle), state.StoredVertex(P.Documents, P.Target), P.Label, "{}", 1L),
         new(P.RootDirect, state.StoredVertex(P.Projects, P.Root), state.StoredVertex(P.Documents, P.Direct), P.Label, "{}", 1L),
         new(P.RootMiddle, state.StoredVertex(P.Projects, P.Root), state.StoredVertex(P.Projects, P.Middle), P.Label, "{}", 1L)]);
}
