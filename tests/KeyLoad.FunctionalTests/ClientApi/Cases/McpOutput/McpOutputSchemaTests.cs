using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>AC-MCP-007: canonical structured-result and safe-error wrapper schemas.</summary>
internal sealed class McpOutputSchemaTests
{
    private const string Type = "type";
    private const string Title = "title";
    private const string Status = "status";
    private const string Detail = "detail";
    private const string ErrorCodeKey = "errorCode";
    private const string FabricatedStatus = "statusCode";
    private const string SafeDetail = "A safe bounded failure.";
    private const string AdditionalProperties = "additionalProperties";
    private const string ObjectType = "object";
    private const string Durability = "durability";

    /// <summary>Every structured result declares closed and disjoint success and failure envelopes.</summary>
    [Test]
    public async Task AcMcp007EveryOutputHasDisjointSuccessAndSafeFailureObjects()
    {
        foreach (var descriptor in McpOperationCatalog.Entries)
        {
            var branches = descriptor.OutputSchema.GetProperty(McpSchemaInspector.OneOf);
            var success = branches[0];
            var failure = branches[1];
            await Assert.That(success.GetProperty(McpSchemaInspector.Required).EnumerateArray()
                    .Select(item => item.GetString() ?? throw new InvalidOperationException()))
                .IsEquivalentTo(new[] { McpSchemaInspector.Result, McpSchemaInspector.RequestId });
            await Assert.That(failure.GetProperty(McpSchemaInspector.Required).EnumerateArray()
                    .Select(item => item.GetString() ?? throw new InvalidOperationException()))
                .IsEquivalentTo(new[] { McpSchemaInspector.Error, McpSchemaInspector.RequestId });
            await Assert.That(success.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
            await Assert.That(failure.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
            var id = failure.GetProperty(McpSchemaInspector.Properties).GetProperty(McpSchemaInspector.RequestId);
            await Assert.That(McpSchemaInspector.HasType(id, McpSchemaInspector.Null)).IsTrue();
        }
    }

    /// <summary>The safe error schema matches the actual owning converter's five wire fields.</summary>
    [Test]
    public async Task AcMcp007SafeErrorSchemaMatchesTheActualManagedCodeProblemConverter()
    {
        var schema = McpSchemaFactory.CreateOutput(typeof(bool), false).GetProperty(McpSchemaInspector.Defs)
            .GetProperty(McpSchemaInspector.Error);
        var actual = JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.PermissionDenied, SafeDetail), JsonDefaults.Options);
        var names = new[] { Type, Title, Status, Detail, ErrorCodeKey };
        await Assert.That(actual.EnumerateObject().Select(property => property.Name)).IsEquivalentTo(names);
        await Assert.That(schema.GetProperty(McpSchemaInspector.Properties).EnumerateObject().Select(property => property.Name)).IsEquivalentTo(names);
        await Assert.That(schema.GetProperty(McpSchemaInspector.Required).EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidOperationException())).IsEquivalentTo(names);
        await Assert.That(actual.TryGetProperty(FabricatedStatus, out _)).IsFalse();
        await Assert.That(schema.GetProperty(AdditionalProperties).ValueKind).IsEqualTo(JsonValueKind.False);
    }

    /// <summary>Absent-object reads retain nullable domain fields in nonnull responses; dynamic SQL retains its actual root shape.</summary>
    [Test]
    public async Task AcMcp007AbsentReadsAndDynamicSqlAllowRootResultNull()
    {
        foreach (var descriptor in McpOperationCatalog.Entries)
        {
            var result = descriptor.OutputSchema.GetProperty(McpSchemaInspector.OneOf)[0]
                .GetProperty(McpSchemaInspector.Properties).GetProperty(McpSchemaInspector.Result);
            var expected = descriptor.Name is SqlOperationProtocol.ToolName or McpCatalogExpectations.CredentialsConfigure;
            await Assert.That(McpSchemaInspector.HasType(result, McpSchemaInspector.Null)).IsEqualTo(expected);
            var field = (NullableDomainField(descriptor.Name)).Value;
            if (field is null)
            {
                continue;
            }
            var (_, response) = ResultSchema(descriptor.Name);
            var properties = response.GetProperty(McpSchemaInspector.Properties);
            await Assert.That(properties.EnumerateObject().Select(property => property.Name))
                .IsEquivalentTo(new[] { field });
            await Assert.That(McpSchemaInspector.HasType(properties.GetProperty(field), McpSchemaInspector.Null)).IsTrue();
        }
    }

    /// <summary>AC-MCP-007: actual result schemas retain primitive, array, DTO, and enum shapes.</summary>
    [Test]
    public async Task AcMcp007RepresentativeCanonicalResultTypesRemainDescribed()
    {
        var credentials = ResultSchema(McpCatalogExpectations.CredentialsConfigure);
        await Assert.That(ResolvesToType(credentials.Root, credentials.Node, McpSchemaInspector.Null)).IsTrue();

        var seriesSchema = ResultSchema(McpCatalogExpectations.SeriesRead);
        await Assert.That(ResolvesToType(seriesSchema.Root, seriesSchema.Node, McpSchemaInspector.Array)).IsTrue();
        var pointSchema = seriesSchema.Node.GetProperty(McpSchemaInspector.Items);
        await Assert.That(ResolvesToType(seriesSchema.Root, pointSchema, ObjectType)).IsTrue();

        var ranked = ResultSchema(McpCatalogExpectations.SearchExecute);
        await Assert.That(ResolvesToType(ranked.Root, ranked.Node, McpSchemaInspector.Array)).IsTrue();
        var rankedItems = ranked.Node.GetProperty(McpSchemaInspector.Items);
        await Assert.That(ResolvesToType(ranked.Root, rankedItems, ObjectType)).IsTrue();

        var receipt = ResultSchema(McpCatalogExpectations.DocumentsCommit);
        await Assert.That(ResolvesToType(receipt.Root, receipt.Node, ObjectType)).IsTrue();
        var durability = receipt.Node.GetProperty(McpSchemaInspector.Properties).GetProperty(Durability);
        await Assert.That(HasEnumSchema(receipt.Root, durability)).IsTrue();
    }

    [Test]
    public async Task CredentialNoDataProjectionPreservesTrueOutcomeAndExactSameIdReplay()
    {
        using var database = new TestDatabase();
        var commandId = Guid.NewGuid();
        var credential = global::KeyLoad.Core.AuthorizationExecution.Credential("status-execution-key", "root", "status-execution-secret");
        var request = new ConfigureApiKeyRequest(credential);
        var original = database.Submit(OperationKind.ConfigureApiKey, request, id: commandId);
        await Assert.That(Errors.Code(original.Problem).Value).IsNull();
        await Assert.That(original.Problem?.Detail).IsNull();
        await Assert.That(original.Get<bool>()).IsTrue();
        var originalResult = OrleansSerialization.Serialize(original);
        var entry = database.ReadStoredOutcome(commandId);
        var entryBytes = OrleansSerialization.Serialize(entry);
        var outcome = database.Store.Read(view => view.ReadOwnedValue(global::KeyLoad.Core.KeySpace.GlobalOutcome("root", commandId)));
        var key = database.Store.Read(view => view.ReadOwnedValue(global::KeyLoad.Core.KeySpace.ApiKey(credential.Id)));
        await Assert.That(outcome).IsNotNull();
        await Assert.That(key).IsNotNull();
        await Assert.That((PublicReadResponse.Create(null, OperationKind.ConfigureApiKey, original.Value)).Value).IsNull();
        var replay = database.Submit(OperationKind.ConfigureApiKey, request, id: commandId);
        await Assert.That(replay.Get<bool>()).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(originalResult)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(database.ReadStoredOutcome(commandId)).SequenceEqual(entryBytes)).IsTrue();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(global::KeyLoad.Core.KeySpace.GlobalOutcome("root", commandId)))!
            .SequenceEqual(outcome!)).IsTrue();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(global::KeyLoad.Core.KeySpace.ApiKey(credential.Id)))!
            .SequenceEqual(key!)).IsTrue();
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
{ _ = (PublicReadResponse.Create(null, OperationKind.ConfigureApiKey, false)).Value; });
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(global::KeyLoad.Core.KeySpace.ApiKey(credential.Id)))!
            .SequenceEqual(key!)).IsTrue();
    }

    private static (bool HasValue, string? Value) NullableDomainField(string operationName) => operationName switch
    {
        McpCatalogExpectations.DocumentsGet => (true, (string?)("document")),
        McpCatalogExpectations.MessagesInspect => (true, (string?)("message")),
        McpCatalogExpectations.QueueTransferInspect => (true, (string?)("transfer")),
        McpCatalogExpectations.QueueTransferReceipt => (true, (string?)("receipt")),
        McpCatalogExpectations.ScheduleInspect => (true, (string?)("schedule")),
        McpCatalogExpectations.SagaInspect => (true, (string?)("saga")),
        _ => (false, default(string?))
    };

    private static (JsonElement Root, JsonElement Node) ResultSchema(string operationName)
    {
        var descriptor = McpOperationCatalog.Entries.Single(item => item.Name == operationName);
        var result = descriptor.OutputSchema.GetProperty(McpSchemaInspector.OneOf)[0]
            .GetProperty(McpSchemaInspector.Properties).GetProperty(McpSchemaInspector.Result);
        if (operationName == McpCatalogExpectations.CredentialsConfigure)
        {
            return (descriptor.OutputSchema, result);
        }
        var reference = McpSchemaInspector.References(result).First();
        return (descriptor.OutputSchema, McpSchemaInspector.Resolve(descriptor.OutputSchema, reference));
    }

    private static bool ResolvesToType(JsonElement root, JsonElement schema, string type)
    {
        if (McpSchemaInspector.HasType(schema, type))
        {
            return true;
        }
        return McpSchemaInspector.References(schema).Any(pointer => McpSchemaInspector.HasType(McpSchemaInspector.Resolve(root, pointer), type));
    }

    private static bool HasEnumSchema(JsonElement root, JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(McpSchemaInspector.Enum, out _))
        {
            return true;
        }
        if (schema.ValueKind == JsonValueKind.Array)
        {
            return schema.EnumerateArray().Any(item => HasEnumSchema(root, item));
        }
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        return schema.EnumerateObject().Where(property => property.Name != McpSchemaInspector.Ref)
                .Any(property => HasEnumSchema(root, property.Value)) ||
            McpSchemaInspector.References(schema).Any(pointer => HasEnumSchema(root, McpSchemaInspector.Resolve(root, pointer)));
    }
}
