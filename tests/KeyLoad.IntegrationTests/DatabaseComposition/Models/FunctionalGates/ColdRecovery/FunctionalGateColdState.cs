using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateColdState(PartitionRef Partition, EntityRef Anchor,
    McpPersistedIdentity Writer, McpPersistedIdentity Reader, McpPersistedIdentity QueueOperator,
    FunctionalGateColdModel[] Models, AdminNodeSnapshot[] Analytics)
{
    internal FunctionalGateReferenceBindings ReferenceBindings { get; init; } = new();
    internal FunctionalGateAnalyticsColdUser[] UserAnalytics { get; init; } = [];
    internal McpPersistedIdentity[] AdditionalIdentities { get; init; } = [];
    internal FunctionalGateColdWorkload[] Workloads { get; init; } = [];
}
