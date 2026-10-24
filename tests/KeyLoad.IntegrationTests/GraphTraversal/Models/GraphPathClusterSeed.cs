using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal sealed record GraphPathClusterSeed(
    PartitionRef Partition,
    EntityRef Source,
    EntityRef FirstBranch,
    EntityRef SecondBranch,
    EntityRef Target,
    EntityRef HiddenSource,
    EntityRef HiddenIntermediate,
    EntityRef HiddenTarget,
    EntityRef Isolated,
    McpPersistedIdentity Reader)
{
    public override string ToString() => "GraphPathClusterSeed";
}
