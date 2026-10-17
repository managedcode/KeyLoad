using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class McpRemoteGraphCatalogExpectations
{
    private const string AdditionalProperties = "additionalProperties";
    internal static global::System.Collections.Immutable.ImmutableArray<(string Name, string Route, GrainReadKind? ReadKind, OperationKind? CommandKind)> Entries
    {
        get;
    } =
    [
        ("keyload_graph_remote_upsert", "/v1/graph/remote/upsert", null, OperationKind.UpsertRemoteGraphEdge),
        ("keyload_graph_remote_delete", "/v1/graph/remote/delete", null, OperationKind.DeleteRemoteGraphEdge),
        ("keyload_graph_remote_deliver", "/v1/graph/remote/deliver", null, OperationKind.DeliverRemoteGraphEdge),
        ("keyload_graph_remote_incoming", "/v1/graph/remote/incoming", GrainReadKind.RemoteGraphIncomingParent, null)
    ];

    internal static async Task RequireAsync()
    {
        foreach (var expected in Entries)
        {
            var actual = McpOperationCatalog.Entries.Single(item => item.Name == expected.Name);
            await Assert.That(actual.Route).IsEqualTo(expected.Route);
            await Assert.That(actual.ReadKind).IsEqualTo(expected.ReadKind);
            await Assert.That(actual.CommandKind).IsEqualTo(expected.CommandKind);
            var incoming = expected.Name == "keyload_graph_remote_incoming";
            await Assert.That(actual.ReadOnly).IsEqualTo(incoming);
            await Assert.That(actual.Idempotent).IsTrue();
            await Assert.That(actual.Destructive).IsEqualTo(!incoming);
            await Assert.That(actual.Description.Contains(McpToolDescriptions.StableRetry, StringComparison.Ordinal)).IsEqualTo(!incoming);
            var tool = actual.CreateTool();
            await Assert.That(tool.Annotations!.ReadOnlyHint).IsEqualTo(incoming);
            await Assert.That(tool.Annotations.IdempotentHint).IsTrue();
            await Assert.That(tool.Annotations.DestructiveHint).IsEqualTo(!incoming);
            await Assert.That(Names(actual.InputSchema.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(new[] { "request" });
            var request = McpSchemaInspector.DefinedRequest(actual.InputSchema);
            await Assert.That(request.GetProperty(AdditionalProperties).ValueKind)
                .IsEqualTo(global::System.Text.Json.JsonValueKind.False);
            await Assert.That(Names(request.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(RequestFields(expected.Name));
            var result = actual.OutputSchema.GetProperty(McpSchemaInspector.Defs).GetProperty(McpSchemaInspector.Result);
            await Assert.That(Names(result.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(incoming
                ? new[] { "version", "rows", "targetCut", "projection" }
                : new[] { "commandId", "sourceReceipt", "deliveries" });
        }
    }

    private static string[] RequestFields(string name) => name switch
    {
        "keyload_graph_remote_upsert" => ["commandId", "sourcePartition", "edge"],
        "keyload_graph_remote_delete" => ["commandId", "sourcePartition", "graph", "edgeId", "expectedRevision"],
        "keyload_graph_remote_deliver" => ["commandId", "sourcePartition", "graph", "edgeId", "destination", "expectedRevision"],
        "keyload_graph_remote_incoming" => ["version", "target", "graph", "limit"],
        _ => throw new InvalidOperationException(name)
    };

    private static string[] Names(global::System.Text.Json.JsonElement node)
        => [.. node.EnumerateObject().Select(property => property.Name)];
}
