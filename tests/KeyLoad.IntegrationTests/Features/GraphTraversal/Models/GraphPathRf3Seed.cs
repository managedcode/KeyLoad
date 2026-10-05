using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed record GraphPathRf3Seed(
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
    public override string ToString() => "GraphPathRf3Seed";
}
