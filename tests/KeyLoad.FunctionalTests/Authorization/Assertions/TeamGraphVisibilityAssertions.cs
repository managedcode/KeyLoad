using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class TeamGraphVisibilityAssertions
{
    internal const string Collection = "team-vertices";
    internal const string Graph = "team-links";
    private const string A = "a";
    private const string B = "b";
    private const string C = "c";
    private const string AB = "ab";
    private const string BC = "bc";
    private const string Label = "next";
    private const string EmptyJson = "{}";
    private const long InitialRevision = 1;

    internal static void Seed(TestDatabase db)
    {
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
        db.Commit(new PutDocument(Collection, A, EmptyJson, Access: new(TeamRowPolicyFlow.Reader)),
            new PutDocument(Collection, B, EmptyJson, Access: new(TeamId: TeamRowPolicyFlow.Team)),
            new PutDocument(Collection, C, EmptyJson, Access: new(TeamRowPolicyFlow.Reader)));
        db.Commit(new UpsertEdge(Graph, AB, Vertex(db.Partition, A), Vertex(db.Partition, B), Label),
            new UpsertEdge(Graph, BC, Vertex(db.Partition, B), Vertex(db.Partition, C), Label));
    }

    internal static async Task ReadAsync(TestDatabase creation, DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        bool teamVisible, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var result = database.Traverse(TeamRowPolicyFlow.Reader, partition, Graph, Vertex(partition, A), cancellationToken: token);
        var first = creation.ExpectedReference(Vertex(partition, A));
        var second = creation.ExpectedReference(Vertex(partition, B));
        var third = creation.ExpectedReference(Vertex(partition, C));
        EntityRef[] vertices = teamVisible ? [first, second, third] : [first];
        EdgeRecord[] edges = teamVisible ? [new(AB, first, second, Label, EmptyJson, InitialRevision),
            new(BC, second, third, Label, EmptyJson, InitialRevision)] : [];
        await Assert.That(JsonDefaults.Serialize(result.Vertices).AsSpan().SequenceEqual(JsonDefaults.Serialize(vertices))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(result.Edges).AsSpan().SequenceEqual(JsonDefaults.Serialize(edges))).IsTrue();
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    internal static async Task RevokedAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Traverse(TeamRowPolicyFlow.Reader,
            partition, Graph, Vertex(partition, A), cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
    }

    private static EntityRef Vertex(PartitionRef partition, string id) => new(partition, Collection, id);
}
