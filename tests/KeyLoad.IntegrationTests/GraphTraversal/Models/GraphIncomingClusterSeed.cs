using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal sealed record GraphIncomingClusterSeed(
    PartitionRef TargetPartition,
    PartitionRef FirstSourcePartition,
    PartitionRef SecondSourcePartition,
    EntityRef Target,
    EntityRef LocalSource,
    EntityRef FirstSource,
    EntityRef SecondSource,
    McpPersistedIdentity Reader,
    McpPersistedIdentity TargetOnlyReader,
    string Graph)
{
    public override string ToString() => "GraphIncomingClusterSeed";
}
