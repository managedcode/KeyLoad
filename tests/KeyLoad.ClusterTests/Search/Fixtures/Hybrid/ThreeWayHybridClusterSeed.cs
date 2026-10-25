namespace KeyLoad.ClusterTests.Search;

internal static class ThreeWayHybridClusterSeed
{
    internal static CommandRequest Create(PartitionRef partition)
    {
        var mutations = new List<Mutation>
        {
            new PutDocument(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.A,
                "{\"body\":\"signal signal signal filler\"}"),
            new PutDocument(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.B,
                "{\"body\":\"signal signal filler filler\"}"),
            new PutDocument(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.C,
                "{\"body\":\"signal filler filler filler\"}"),
            new PutDocument(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.D,
                "{\"body\":\"filler filler filler filler\"}"),
            new PutDocument(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.E,
                "{\"body\":\"filler filler filler filler\"}"),
            new PutVector(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.A,
                ThreeWayHybridClusterScenario.VectorField, [0.8f, 0.6f], ThreeWayHybridClusterScenario.Space, 1),
            new PutVector(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.B,
                ThreeWayHybridClusterScenario.VectorField, [1, 0], ThreeWayHybridClusterScenario.Space, 1),
            new PutVector(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.C,
                ThreeWayHybridClusterScenario.VectorField, [0, 1], ThreeWayHybridClusterScenario.Space, 1),
            new PutVector(ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.D,
                ThreeWayHybridClusterScenario.VectorField, [-1, 0], ThreeWayHybridClusterScenario.Space, 1),
            new PutDocument(ThreeWayHybridClusterScenario.Projects, ThreeWayHybridClusterScenario.RetrieveRoot, "{}"),
            new PutDocument(ThreeWayHybridClusterScenario.Projects, ThreeWayHybridClusterScenario.ScopeRoot, "{}"),
            new PutDocument(ThreeWayHybridClusterScenario.Projects, "middle-one", "{}"),
            new PutDocument(ThreeWayHybridClusterScenario.Projects, "middle-two", "{}"),
            new PutDocument(ThreeWayHybridClusterScenario.Projects, "middle-three", "{}"),
            new PutDocument(ThreeWayHybridClusterScenario.Projects, "middle-four", "{}"),
            new PutDocument(ThreeWayHybridClusterScenario.Projects, "context", "{\"body\":\"context\"}")
        };
        AddGraphEdges(partition, mutations);
        return new(Guid.NewGuid(), partition, [.. mutations]);
    }

    private static void AddGraphEdges(PartitionRef partition, List<Mutation> mutations)
    {
        var root = Vertex(partition, ThreeWayHybridClusterScenario.RetrieveRoot);
        var scope = Vertex(partition, ThreeWayHybridClusterScenario.ScopeRoot);
        var a = new EntityRef(partition, ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.A);
        var b = new EntityRef(partition, ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.B);
        var c = new EntityRef(partition, ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.C);
        var d = new EntityRef(partition, ThreeWayHybridClusterScenario.Documents, ThreeWayHybridClusterScenario.D);
        var midOne = Vertex(partition, "middle-one");
        var midTwo = Vertex(partition, "middle-two");
        var midThree = Vertex(partition, "middle-three");
        var midFour = Vertex(partition, "middle-four");
        var context = Vertex(partition, "context");
        mutations.AddRange(
        [
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "scope-a", scope, a, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "scope-b", scope, b, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "scope-c", scope, c, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "retrieve-d", root, d, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "retrieve-m1", root, midOne, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "m1-c", midOne, c, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "retrieve-m2", root, midTwo, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "m2-c", midTwo, c, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "retrieve-m3", root, midThree, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "m3-m4", midThree, midFour, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "m4-b", midFour, b, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "cycle-c", c, midOne, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.Graph, "cycle-b", b, root, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.ExpansionGraph, "a-context", a, context, "related"),
            new UpsertEdge(ThreeWayHybridClusterScenario.ExpansionGraph, "b-context", b, context, "related")
        ]);
    }

    private static EntityRef Vertex(PartitionRef partition, string id)
        => new(partition, ThreeWayHybridClusterScenario.Projects, id);
}
