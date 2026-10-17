using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class MetadataBootstrapRf3State(ClusterFixture fixture)
{
    internal const string Collection = "metadata-bootstrap-documents";
    private const string InitialPartitionKey = "before-loss";
    internal const string Json = "{\"metadata\":true}";
    internal static readonly string[] Nodes = ["node1", "node2", "node3"];
    internal ClusterFixture Fixture { get; } = fixture;
    internal PartitionRef First { get; } = new("meta-rf3-tenant", "meta-rf3-db", "meta-rf3-domain", InitialPartitionKey);
    internal PartitionRef Second => First with { PartitionKey = "after-loss" };
    internal PartitionRef Third => First with { PartitionKey = "after-repair" };
    internal string ActiveNode { get; set; } = "node1";
    internal AtomicPartitionPlacementResolution Owner { get; set; } = null!;
    internal McpPersistedIdentity Identity { get; set; } = null!;
    internal List<(Guid Id, BindAtomicPartitionPlacementRequest Request, string Credential)> Binds { get; } = [];
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Commands { get; } = [];
    internal HashSet<string> Stopped { get; } = new(StringComparer.Ordinal);
    internal MetadataBootstrapRf3Cold Cold { get; } = new(fixture);
    internal Dictionary<string, NodeStatus> Statuses { get; } = new(StringComparer.Ordinal);
    internal Queue<string> AuthorityObservations { get; } = [];
    internal NodeStatus BeforeLoss { get; set; } = null!;
    internal NodeStatus AfterLoss { get; set; } = null!;
    internal string? LostNode
    {
        get; set;
    }
    internal string? FaultScenario
    {
        get; set;
    }
}
