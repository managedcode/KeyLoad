using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.ClientApi;

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
    private const string OwnershipEpoch = "ownershipEpoch";
    private const long DefaultOwnershipEpoch = 1;
    private const string AdditionalProperties = "additionalProperties";
    private const string BooleanType = "boolean";
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

    /// <summary>Optional ownership epoch retains its canonical constructor default without becoming required.</summary>
    [Test]
    public async Task AcMcp003CanonicalCommandConstructorDefaultRemainsDocumented()
    {
        var input = McpSchemaFactory.CreateInput(typeof(CommandRequest), false);
        var request = McpSchemaInspector.DefinedRequest(input);
        await Assert.That(request.GetProperty(McpSchemaInspector.Properties).GetProperty(OwnershipEpoch)
            .GetProperty(McpSchemaInspector.Default).GetInt64()).IsEqualTo(DefaultOwnershipEpoch);
        await Assert.That(request.GetProperty(McpSchemaInspector.Required).EnumerateArray()
            .Any(value => value.GetString() == OwnershipEpoch)).IsFalse();
    }

    /// <summary>The eight absent-object reads and dynamic SQL adapter allow a null root result.</summary>
    [Test]
    public async Task AcMcp007AbsentReadsAndDynamicSqlAllowRootResultNull()
    {
        foreach (var descriptor in McpOperationCatalog.Entries)
        {
            var result = descriptor.OutputSchema.GetProperty(McpSchemaInspector.OneOf)[0]
                .GetProperty(McpSchemaInspector.Properties).GetProperty(McpSchemaInspector.Result);
            var expected = descriptor.Name is McpCatalogExpectations.DocumentsGet or McpCatalogExpectations.MessagesInspect
                or BlobAgentCases.Metadata or BlobAgentCases.UploadInfo or SqlOperationProtocol.ToolName
                or McpCatalogExpectations.QueueTransferInspect or McpCatalogExpectations.QueueTransferReceipt
                or McpCatalogExpectations.ScheduleInspect or McpCatalogExpectations.SagaInspect;
            await Assert.That(McpSchemaInspector.HasType(result, McpSchemaInspector.Null)).IsEqualTo(expected);
        }
    }

    /// <summary>AC-MCP-007: actual result schemas retain primitive, array, DTO, and enum shapes.</summary>
    [Test]
    public async Task AcMcp007RepresentativeCanonicalResultTypesRemainDescribed()
    {
        var credentials = ResultSchema(McpCatalogExpectations.CredentialsConfigure);
        await Assert.That(ResolvesToType(credentials.Root, credentials.Node, BooleanType)).IsTrue();

        var samples = ResultSchema(McpCatalogExpectations.SeriesRead);
        await Assert.That(ResolvesToType(samples.Root, samples.Node, McpSchemaInspector.Array)).IsTrue();
        var sampleItems = samples.Node.GetProperty(McpSchemaInspector.Items);
        await Assert.That(ResolvesToType(samples.Root, sampleItems, ObjectType)).IsTrue();

        var ranked = ResultSchema(McpCatalogExpectations.SearchExecute);
        await Assert.That(ResolvesToType(ranked.Root, ranked.Node, McpSchemaInspector.Array)).IsTrue();
        var rankedItems = ranked.Node.GetProperty(McpSchemaInspector.Items);
        await Assert.That(ResolvesToType(ranked.Root, rankedItems, ObjectType)).IsTrue();

        var receipt = ResultSchema(McpCatalogExpectations.DocumentsCommit);
        await Assert.That(ResolvesToType(receipt.Root, receipt.Node, ObjectType)).IsTrue();
        var durability = receipt.Node.GetProperty(McpSchemaInspector.Properties).GetProperty(Durability);
        await Assert.That(HasEnumSchema(receipt.Root, durability)).IsTrue();
    }

    private static (JsonElement Root, JsonElement Node) ResultSchema(string operationName)
    {
        var descriptor = McpOperationCatalog.Entries.Single(item => item.Name == operationName);
        var result = descriptor.OutputSchema.GetProperty(McpSchemaInspector.OneOf)[0]
            .GetProperty(McpSchemaInspector.Properties).GetProperty(McpSchemaInspector.Result);
        var reference = McpSchemaInspector.References(result).First();
        return (descriptor.OutputSchema, McpSchemaInspector.Resolve(descriptor.OutputSchema, reference));
    }

    private static bool ResolvesToType(JsonElement root, JsonElement schema, string type)
    {
        if (McpSchemaInspector.HasType(schema, type))
        { return true; }
        return McpSchemaInspector.References(schema).Any(pointer => McpSchemaInspector.HasType(McpSchemaInspector.Resolve(root, pointer), type));
    }

    private static bool HasEnumSchema(JsonElement root, JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(McpSchemaInspector.Enum, out _))
        { return true; }
        if (schema.ValueKind == JsonValueKind.Array)
        {
            return schema.EnumerateArray().Any(item => HasEnumSchema(root, item));
        }
        if (schema.ValueKind != JsonValueKind.Object)
        { return false; }
        return schema.EnumerateObject().Where(property => property.Name != McpSchemaInspector.Ref)
                .Any(property => HasEnumSchema(root, property.Value)) ||
            McpSchemaInspector.References(schema).Any(pointer => HasEnumSchema(root, McpSchemaInspector.Resolve(root, pointer)));
    }
}
