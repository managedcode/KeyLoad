namespace KeyLoad.IntegrationTests.ClientApi;

internal sealed record McpAnalyticsDropDiscoveryState(PartitionRef Partition,
    ResourceDefinition Table, EntityRef Anchor, EntityRef OldRow);
