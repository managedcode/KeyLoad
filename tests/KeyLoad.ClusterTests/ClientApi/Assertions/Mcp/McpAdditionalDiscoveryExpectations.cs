using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.ClientApi;

internal static class McpAdditionalDiscoveryExpectations
{
    internal static ImmutableArray<McpToolExpectation> Entries
    {
        get;
    } =
    [
        new("keyload_query_distributed_search", true, true, false, McpExpectedBody.Object, false,
            ["partitions", "search"],
            ["hits", "leaves", "statisticsEpoch", "complete"],
            BodyPropertyFields: ["partitions", "search"]),
        new("keyload_inbox_commit", false, true, true, McpExpectedBody.Object, false,
            ["commandId", "target", "source", "messageId", "deliveryGeneration", "handlerScope", "executionGeneration", "effects"],
            ["receipt", "alreadyProcessed", "originalEffectsToken"],
            BodyPropertyFields: ["commandId", "target", "source", "messageId", "deliveryGeneration", "handlerScope", "executionGeneration", "effects"])
    ];
}
