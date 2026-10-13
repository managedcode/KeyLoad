using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class McpRemoteGraphDiscoveryExpectations
{
    internal static ImmutableArray<McpToolExpectation> Entries { get; } =
    [
        new("keyload_graph_remote_upsert", false, true, true, McpExpectedBody.Object, false,
            ["commandId", "sourcePartition", "edge"],
            ["commandId", "sourceReceipt", "deliveries"],
            BodyPropertyFields: ["commandId", "sourcePartition", "edge"]),
        new("keyload_graph_remote_delete", false, true, true, McpExpectedBody.Object, false,
            ["commandId", "sourcePartition", "graph", "edgeId"],
            ["commandId", "sourceReceipt", "deliveries"],
            BodyPropertyFields: ["commandId", "sourcePartition", "graph", "edgeId", "expectedRevision"]),
        new("keyload_graph_remote_deliver", false, true, true, McpExpectedBody.Object, false,
            ["commandId", "sourcePartition", "graph", "edgeId", "destination", "expectedRevision"],
            ["commandId", "sourceReceipt", "deliveries"],
            BodyPropertyFields: ["commandId", "sourcePartition", "graph", "edgeId", "destination", "expectedRevision"]),
        new("keyload_graph_remote_incoming", true, true, false, McpExpectedBody.Object, false,
            ["version", "target", "graph", "limit"],
            ["version", "rows", "targetCut", "projection"],
            BodyPropertyFields: ["version", "target", "graph", "limit"])
    ];
}
