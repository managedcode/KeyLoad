namespace KeyLoad.FunctionalTests.ClientApi;

internal static class QueueDispatchMcpCatalogAssertions
{
    internal const string Tool = "keyload_queue_dispatch_control";
    internal const string Route = "/admin/queues/dispatch";
    private const int Kind = 50;
    private const int FirstField = 0;
    private const int FirstSuccessBranch = 0;
    private const int SchemaVersionFieldIndex = 3;
    private const string AdditionalProperties = "additionalProperties";
    private const string Required = "required";
    private const string CommandId = "commandId";
    private const string Request = "request";
    private const string StringType = "string";
    private const string IntegerType = "integer";
    private const string BooleanType = "boolean";
    private const string ChangedName = "changed-queue-dispatch-tool";
    private static readonly string[] RequestFields =
        ["tenantId", "databaseId", "queue", "expectedSchemaVersion", "paused"];
    private static readonly string[] ResultFields =
        ["name", "kind", "transactionDomainId", "indexes", "fieldPolicies", "headerPolicies",
         "queuePolicy", "eventRetention", "authority", "schemaVersion", "paused",
         "relationalSchema", "vectorProfiles", "inboxPolicy", "analyticsSchema", "filePolicy"];

    internal static async Task RequireAsync()
    {
        await Assert.That(global::KeyLoad.Server.McpOperationCatalog.TryGet(Tool, out var found)).IsTrue();
        var actual = found ?? throw new InvalidOperationException(Tool);
        await Assert.That(actual.Route).IsEqualTo(Route);
        await Assert.That(actual.ReadKind).IsNull();
        await Assert.That(actual.CommandKind).IsEqualTo((OperationKind?)OperationKind.SetQueueDispatch);
        await Assert.That((int?)actual.CommandKind).IsEqualTo(Kind);
        await Assert.That(actual.IsAdapter).IsFalse();
        await Assert.That(actual.ReadOnly).IsFalse();
        await Assert.That(actual.Idempotent).IsTrue();
        await Assert.That(actual.Destructive).IsTrue();
        await Assert.That(actual.Description.Contains(global::KeyLoad.Server.McpToolDescriptions.StableRetry, StringComparison.Ordinal)).IsTrue();
        var root = actual.InputSchema;
        await Assert.That(root.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.False);
        await Assert.That(Names(root.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(new[] { CommandId, Request });
        await Assert.That(Strings(root.GetProperty(Required))).IsEquivalentTo(new[] { CommandId, Request });
        var identity = root.GetProperty(McpSchemaInspector.Properties).GetProperty(CommandId);
        await Assert.That(McpSchemaInspector.HasType(identity, StringType)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(identity, McpSchemaInspector.Null)).IsFalse();
        var request = McpSchemaInspector.DefinedRequest(root);
        await Assert.That(request.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.False);
        await Assert.That(Names(request.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(RequestFields);
        await Assert.That(Strings(request.GetProperty(Required))).IsEquivalentTo(RequestFields);
        for (var index = FirstField; index < RequestFields.Length; index++)
        {
            var field = request.GetProperty(McpSchemaInspector.Properties).GetProperty(RequestFields[index]);
            await Assert.That(McpSchemaInspector.HasType(field, index < SchemaVersionFieldIndex ? StringType : index == SchemaVersionFieldIndex ? IntegerType : BooleanType)).IsTrue();
            await Assert.That(McpSchemaInspector.HasType(field, McpSchemaInspector.Null)).IsFalse();
        }
        var result = actual.OutputSchema.GetProperty(McpSchemaInspector.Defs).GetProperty(McpSchemaInspector.Result);
        await Assert.That(Names(result.GetProperty(McpSchemaInspector.Properties))).IsEquivalentTo(ResultFields);
        var success = actual.OutputSchema.GetProperty(McpSchemaInspector.OneOf)[FirstSuccessBranch]
            .GetProperty(McpSchemaInspector.Properties).GetProperty(McpSchemaInspector.Result);
        await Assert.That(McpSchemaInspector.HasType(success, McpSchemaInspector.Null)).IsFalse();
        var first = actual.CreateTool();
        var second = actual.CreateTool();
        first.Name = ChangedName;
        first.Annotations!.ReadOnlyHint = true;
        first.Annotations.IdempotentHint = false;
        first.Annotations.DestructiveHint = false;
        await Assert.That(second.Name).IsEqualTo(Tool);
        await Assert.That(second.Annotations!.ReadOnlyHint).IsFalse();
        await Assert.That(second.Annotations.IdempotentHint).IsTrue();
        await Assert.That(second.Annotations.DestructiveHint).IsTrue();
        await Assert.That(second.InputSchema.GetRawText()).IsEqualTo(root.GetRawText());
        await Assert.That(second.OutputSchema!.Value.GetRawText()).IsEqualTo(actual.OutputSchema.GetRawText());
    }

    private static string[] Names(global::System.Text.Json.JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];
    private static string[] Strings(global::System.Text.Json.JsonElement element) => [.. element.EnumerateArray().Select(item => item.GetString()!)];
}
