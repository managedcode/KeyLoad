using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateColdState(PartitionRef Partition, EntityRef Anchor,
    McpPersistedIdentity Writer, McpPersistedIdentity Reader, McpPersistedIdentity QueueOperator,
    FunctionalGateColdModel[] Models, AdminNodeSnapshot[] Nodes)
{
    internal FunctionalGateReferenceBindings ReferenceBindings { get; init; } = new();
    internal FunctionalGateAnalyticsColdUser[] UserAnalytics { get; init; } = [];
    internal McpPersistedIdentity[] AdditionalIdentities { get; init; } = [];
    internal FunctionalGateColdWorkload[] Workloads { get; init; } = [];
}
