namespace KeyLoad.IntegrationTests.Features.Search;

internal static class ThreeWayHybridRf3Seed
{
    internal static CommandRequest Create(PartitionRef partition)
    {
        var mutations = new List<Mutation>
        {
            new PutDocument(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.A,
                "{\"body\":\"signal signal signal filler\"}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.B,
                "{\"body\":\"signal signal filler filler\"}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.C,
                "{\"body\":\"signal filler filler filler\"}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.D,
                "{\"body\":\"filler filler filler filler\"}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.E,
                "{\"body\":\"filler filler filler filler\"}"),
            new PutVector(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.A,
                ThreeWayHybridRf3Scenario.VectorField, [0.8f, 0.6f], ThreeWayHybridRf3Scenario.Space, 1),
            new PutVector(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.B,
                ThreeWayHybridRf3Scenario.VectorField, [1, 0], ThreeWayHybridRf3Scenario.Space, 1),
            new PutVector(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.C,
                ThreeWayHybridRf3Scenario.VectorField, [0, 1], ThreeWayHybridRf3Scenario.Space, 1),
            new PutVector(ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.D,
                ThreeWayHybridRf3Scenario.VectorField, [-1, 0], ThreeWayHybridRf3Scenario.Space, 1),
            new PutDocument(ThreeWayHybridRf3Scenario.Projects, ThreeWayHybridRf3Scenario.RetrieveRoot, "{}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Projects, ThreeWayHybridRf3Scenario.ScopeRoot, "{}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Projects, "middle-one", "{}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Projects, "middle-two", "{}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Projects, "middle-three", "{}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Projects, "middle-four", "{}"),
            new PutDocument(ThreeWayHybridRf3Scenario.Projects, "context", "{\"body\":\"context\"}")
        };
        AddGraphEdges(partition, mutations);
        return new(Guid.NewGuid(), partition, [.. mutations]);
    }

    private static void AddGraphEdges(PartitionRef partition, List<Mutation> mutations)
    {
        var root = Vertex(partition, ThreeWayHybridRf3Scenario.RetrieveRoot);
        var scope = Vertex(partition, ThreeWayHybridRf3Scenario.ScopeRoot);
        var a = new EntityRef(partition, ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.A);
        var b = new EntityRef(partition, ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.B);
        var c = new EntityRef(partition, ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.C);
        var d = new EntityRef(partition, ThreeWayHybridRf3Scenario.Documents, ThreeWayHybridRf3Scenario.D);
        var midOne = Vertex(partition, "middle-one");
        var midTwo = Vertex(partition, "middle-two");
        var midThree = Vertex(partition, "middle-three");
        var midFour = Vertex(partition, "middle-four");
        var context = Vertex(partition, "context");
        mutations.AddRange(
        [
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "scope-a", scope, a, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "scope-b", scope, b, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "scope-c", scope, c, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "retrieve-d", root, d, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "retrieve-m1", root, midOne, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "m1-c", midOne, c, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "retrieve-m2", root, midTwo, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "m2-c", midTwo, c, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "retrieve-m3", root, midThree, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "m3-m4", midThree, midFour, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "m4-b", midFour, b, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "cycle-c", c, midOne, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.Graph, "cycle-b", b, root, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.ExpansionGraph, "a-context", a, context, "related"),
            new UpsertEdge(ThreeWayHybridRf3Scenario.ExpansionGraph, "b-context", b, context, "related")
        ]);
    }

    private static EntityRef Vertex(PartitionRef partition, string id)
        => new(partition, ThreeWayHybridRf3Scenario.Projects, id);
}
