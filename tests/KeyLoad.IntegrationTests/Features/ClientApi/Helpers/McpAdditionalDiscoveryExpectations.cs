using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class McpAdditionalDiscoveryExpectations
{
    internal static ImmutableArray<McpToolExpectation> Entries { get; } =
    [
        new("keyload_admin_partition_move", false, true, true, McpExpectedBody.Object, false,
            ["moveId", "partition", "destinationPhysicalShardId", "expectedPlacementRevision", "mode"],
            ["moveId", "partition", "phase", "sourceOwner", "destinationOwner", "sourceCut", "installedReceipt", "publishedPlacement"],
            BodyPropertyFields: ["moveId", "partition", "destinationPhysicalShardId", "expectedPlacementRevision", "mode"]),
        new("keyload_query_distributed_search", true, true, false, McpExpectedBody.Object, false,
            ["version", "partitions", "search"],
            ["version", "hits", "leaves", "statisticsEpoch", "complete"],
            BodyPropertyFields: ["version", "partitions", "search"]),
        new("keyload_inbox_commit", false, true, true, McpExpectedBody.Object, false,
            ["commandId", "target", "source", "messageId", "deliveryGeneration", "handlerScope", "executionGeneration", "effects"],
            ["receipt", "alreadyProcessed", "originalEffectsToken"],
            BodyPropertyFields: ["commandId", "target", "source", "messageId", "deliveryGeneration", "handlerScope", "executionGeneration", "effects"])
    ];
}
