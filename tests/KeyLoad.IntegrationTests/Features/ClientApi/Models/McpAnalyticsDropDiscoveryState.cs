namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal sealed record McpAnalyticsDropDiscoveryState(PartitionRef Partition,
    ResourceDefinition Table, EntityRef Anchor, EntityRef OldRow);
