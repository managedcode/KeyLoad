namespace KeyLoad.IntegrationTests.GraphTraversal;

internal sealed class GraphCrossAuthorityR41State(GraphIncomingClusterSeed seed)
{
    internal GraphIncomingClusterSeed Seed { get; } = seed;
    internal EntityRef Collision => new(Seed.TargetPartition, GraphIncomingClusterScenario.Collection, Seed.FirstSource.Id);
    internal OutboxStatus[] Outbox { get; set; } = [];
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal bool Hidden
    {
        get; set;
    }
    internal long LocalRevision { get; set; } = 1L;
    internal const string ForeignId = "a-foreign";
    internal const string LocalId = "b-local";
    internal const string CollisionId = "c-collision";
    internal const string Keep = "keep";
    internal const string Skip = "skip";
    internal const string HealthyJson = "{\"public\":\"healthy-local\"}";

    internal EdgeRecord Foreign => new(ForeignId, Seed.LocalSource, Seed.FirstSource, Keep, "{}", 1L);
    internal EdgeRecord Local => new(LocalId, Seed.LocalSource, Seed.Target, Skip,
        LocalRevision == 1L ? "{}" : HealthyJson, LocalRevision);
    internal EdgeRecord Colliding => new(CollisionId, Collision, Seed.Target, Keep, "{}", 1L);
    internal TraverseRequest Request(int vertices = 3, int edges = 2, bool filtered = false)
        => new(Seed.TargetPartition, Seed.Graph, Seed.LocalSource, 3, vertices, edges,
            filtered ? System.Collections.Immutable.ImmutableArray.Create(Keep) : null);
    internal global::KeyLoad.GraphTraversal Expected(bool hidden, bool filtered = false)
    {
        var edges = new List<EdgeRecord>();
        if (!hidden)
        {
            edges.Add(Foreign);
        }
        if (!filtered)
        {
            edges.Add(Local);
        }
        var vertices = edges.Select(edge => edge.To).Append(Seed.LocalSource).Distinct()
            .OrderBy(vertex => vertex.Collection, StringComparer.Ordinal).ThenBy(vertex => vertex.Id, StringComparer.Ordinal);
        return Seed.StoredTraversal(new([.. vertices], [.. edges]));
    }
}
