using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-PMAP-003/004 freezes both generated placement schemas and their effect hints.</summary>
internal sealed class McpPartitionPlacementCatalogTests
{
    private const string Type = "type";
    private const string Object = "object";
    private const string String = "string";
    private const string Integer = "integer";
    private const string Boolean = "boolean";
    private const string Array = "array";
    private const string Properties = "properties";
    private const string Required = "required";
    private const string AdditionalProperties = "additionalProperties";
    private const string Request = "request";
    private const string CommandId = "commandId";
    private const string Result = "result";
    private const string Definitions = "$defs";
    private const string Items = "items";

    [Test]
    public async Task AcPmap003GeneratedPublicAliasesAndFieldIdsStayFrozen()
    {
        await VerifyNativeContractAsync(typeof(BindAtomicPartitionPlacementRequest),
            "keyload.contract.atomic-partition-placement-bind-request.v1",
            ["Version", "ExpectedRevision", "Partition", "PhysicalShardId"]);
        await VerifyNativeContractAsync(typeof(AtomicPartitionPlacementReadRequest),
            "keyload.contract.atomic-partition-placement-read-request.v1", ["Version", "Partition"]);
        await VerifyNativeContractAsync(typeof(AtomicPartitionPlacementResolution),
            "keyload.contract.atomic-partition-placement-resolution.v1",
            ["Version", "Partition", "PhysicalShardId", "Incarnation", "VoterIds", "PlacementEpoch",
                "DirectoryRevision", "Revision", "IsFallback"]);
    }

    [Test]
    public async Task AcPmap003BindHasOneCommandEntryAndExactRetryHints()
    {
        var descriptor = Find(McpCatalogExpectations.AdminPartitionPlacementBind);
        await Assert.That(McpCatalogExpectations.Entries.Count(entry =>
            entry.Name == McpCatalogExpectations.AdminPartitionPlacementBind)).IsEqualTo(1);
        await Assert.That(descriptor.Route).IsEqualTo("/v1/admin/partition-placement/bind");
        await Assert.That(descriptor.CommandKind).IsEqualTo(OperationKind.BindAtomicPartitionPlacement);
        await Assert.That(descriptor.ReadKind).IsNull();
        await AssertHintsAsync(descriptor, readOnly: false, idempotent: true, destructive: false);

        var tool = descriptor.CreateTool();
        await VerifyObjectAsync(tool.InputSchema,
            [Request, CommandId], [Request, CommandId]);
        await VerifyTypeAsync(tool.InputSchema,
            tool.InputSchema.GetProperty(Properties).GetProperty(CommandId), String);
        await VerifyTypeAsync(tool.InputSchema,
            tool.InputSchema.GetProperty(Properties).GetProperty(Request), Object);
        var body = McpSchemaInspector.DefinedRequest(tool.InputSchema);
        var fields = ImmutableArray.Create(McpPartitionPlacementCatalogProtocol.Version,
            McpPartitionPlacementCatalogProtocol.ExpectedRevision, McpPartitionPlacementCatalogProtocol.Partition,
            McpPartitionPlacementCatalogProtocol.PhysicalShardId);
        await VerifyObjectAsync(body, fields, fields);
        var properties = body.GetProperty(Properties);
        await VerifyTypeAsync(tool.InputSchema, properties.GetProperty(McpPartitionPlacementCatalogProtocol.Version), Integer);
        await VerifyTypeAsync(tool.InputSchema, properties.GetProperty(McpPartitionPlacementCatalogProtocol.ExpectedRevision), Integer);
        await VerifyPartitionAsync(tool.InputSchema, properties.GetProperty(McpPartitionPlacementCatalogProtocol.Partition));
        await VerifyTypeAsync(tool.InputSchema, properties.GetProperty(McpPartitionPlacementCatalogProtocol.PhysicalShardId), String);
        var result = Resolve(tool.OutputSchema!.Value,
            tool.OutputSchema.Value.GetProperty(Definitions).GetProperty(Result));
        await VerifyTypeAsync(tool.OutputSchema.Value, result, Boolean);
    }

    [Test]
    public async Task AcPmap003ReadHasOneTypedReadEntryAndCompleteOwnerWitness()
    {
        var descriptor = Find(McpCatalogExpectations.AdminPartitionPlacementRead);
        await Assert.That(McpCatalogExpectations.Entries.Count(entry =>
            entry.Name == McpCatalogExpectations.AdminPartitionPlacementRead)).IsEqualTo(1);
        await Assert.That(descriptor.Route).IsEqualTo("/v1/admin/partition-placement/read");
        await Assert.That(descriptor.ReadKind).IsEqualTo(GrainReadKind.AtomicPartitionPlacement);
        await Assert.That(descriptor.CommandKind).IsNull();
        await AssertHintsAsync(descriptor, readOnly: true, idempotent: true, destructive: false);

        var tool = descriptor.CreateTool();
        await VerifyObjectAsync(tool.InputSchema, [Request], [Request]);
        await VerifyTypeAsync(tool.InputSchema,
            tool.InputSchema.GetProperty(Properties).GetProperty(Request), Object);
        var request = McpSchemaInspector.DefinedRequest(tool.InputSchema);
        var requestFields = ImmutableArray.Create(McpPartitionPlacementCatalogProtocol.Version,
            McpPartitionPlacementCatalogProtocol.Partition);
        await VerifyObjectAsync(request, requestFields, requestFields);
        var inputFields = request.GetProperty(Properties);
        await VerifyTypeAsync(tool.InputSchema, inputFields.GetProperty(McpPartitionPlacementCatalogProtocol.Version), Integer);
        await VerifyPartitionAsync(tool.InputSchema, inputFields.GetProperty(McpPartitionPlacementCatalogProtocol.Partition));

        var output = tool.OutputSchema!.Value;
        var witness = Resolve(output, output.GetProperty(Definitions).GetProperty(Result));
        var fields = ImmutableArray.Create(McpPartitionPlacementCatalogProtocol.Version,
            McpPartitionPlacementCatalogProtocol.Partition, McpPartitionPlacementCatalogProtocol.PhysicalShardId,
            McpPartitionPlacementCatalogProtocol.Incarnation, McpPartitionPlacementCatalogProtocol.VoterIds,
            McpPartitionPlacementCatalogProtocol.PlacementEpoch, McpPartitionPlacementCatalogProtocol.DirectoryRevision,
            McpPartitionPlacementCatalogProtocol.Revision, McpPartitionPlacementCatalogProtocol.IsFallback);
        await VerifyObjectAsync(witness, fields, fields);
        var outputFields = witness.GetProperty(Properties);
        await VerifyTypeAsync(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.Version), Integer);
        await VerifyPartitionAsync(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.Partition));
        await VerifyTypeAsync(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.PhysicalShardId), String);
        await VerifyTypeAsync(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.Incarnation), String);
        await VerifyTypeAsync(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.PlacementEpoch), Integer);
        await VerifyTypeAsync(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.DirectoryRevision), Integer);
        await VerifyTypeAsync(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.Revision), Integer);
        await VerifyTypeAsync(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.IsFallback), Boolean);
        var voters = Resolve(output, outputFields.GetProperty(McpPartitionPlacementCatalogProtocol.VoterIds));
        await Assert.That(voters.GetProperty(Type).GetString()).IsEqualTo(Array);
        await VerifyTypeAsync(output, voters.GetProperty(Items), String);
    }

    private static async Task VerifyNativeContractAsync(Type contract, string alias, string[] fieldNames)
    {
        await Assert.That(contract.IsDefined(typeof(global::Orleans.GenerateSerializerAttribute))).IsTrue();
        var aliasAttribute = contract.GetCustomAttributesData().Single(attribute =>
            attribute.AttributeType == typeof(global::Orleans.AliasAttribute));
        await Assert.That(aliasAttribute.ConstructorArguments.Single().Value).IsEqualTo(alias);
        foreach (var (fieldName, index) in fieldNames.Select((name, position) => (name, position)))
        {
            var property = contract.GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            await Assert.That(property is not null).IsTrue();
            var identifiers = property!.GetCustomAttributesData().Where(attribute =>
                attribute.AttributeType == typeof(global::Orleans.IdAttribute)).ToArray();
            await Assert.That(identifiers.Length).IsEqualTo(1);
            await Assert.That(identifiers[0].ConstructorArguments.Single().Value).IsEqualTo((uint)index);
        }
    }

    private static async Task VerifyPartitionAsync(JsonElement root, JsonElement schema)
    {
        schema = Resolve(root, schema);
        var fields = ImmutableArray.Create(McpPartitionPlacementCatalogProtocol.TenantId,
            McpPartitionPlacementCatalogProtocol.DatabaseId, McpPartitionPlacementCatalogProtocol.TransactionDomainId,
            McpPartitionPlacementCatalogProtocol.PartitionKey);
        await VerifyObjectAsync(schema, fields.Add(McpPartitionPlacementCatalogProtocol.AtomicPartitionId), fields);
        foreach (var field in fields.Add(McpPartitionPlacementCatalogProtocol.AtomicPartitionId))
        { await VerifyTypeAsync(root, schema.GetProperty(Properties).GetProperty(field), String); }
    }

    private static async Task VerifyObjectAsync(JsonElement schema, ImmutableArray<string> properties,
        ImmutableArray<string> required)
    {
        await Assert.That(schema.GetProperty(Type).GetString()).IsEqualTo(Object);
        await Assert.That(schema.GetProperty(AdditionalProperties).GetBoolean()).IsFalse();
        var actual = schema.GetProperty(Properties).EnumerateObject().Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
        await Assert.That(actual.SetEquals(properties)).IsTrue();
        var actualRequired = schema.GetProperty(Required).EnumerateArray().Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        await Assert.That(actualRequired.SetEquals(required)).IsTrue();
    }

    private static async Task VerifyTypeAsync(JsonElement root, JsonElement schema, string expected)
    {
        schema = Resolve(root, schema);
        await Assert.That(McpSchemaInspector.HasType(schema, expected)).IsTrue();
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        return schema.TryGetProperty(McpSchemaInspector.Ref, out var reference)
            ? McpSchemaInspector.Resolve(root, reference.GetString()!) : schema;
    }

    private static async Task AssertHintsAsync(McpOperationDescriptor descriptor, bool readOnly,
        bool idempotent, bool destructive)
    {
        await Assert.That(descriptor.ReadOnly).IsEqualTo(readOnly);
        await Assert.That(descriptor.Idempotent).IsEqualTo(idempotent);
        await Assert.That(descriptor.Destructive).IsEqualTo(destructive);
        var annotations = descriptor.CreateTool().Annotations!;
        await Assert.That(annotations.ReadOnlyHint).IsEqualTo(readOnly);
        await Assert.That(annotations.IdempotentHint).IsEqualTo(idempotent);
        await Assert.That(annotations.DestructiveHint).IsEqualTo(destructive);
    }

    private static McpOperationDescriptor Find(string name)
    {
        if (!McpOperationCatalog.TryGetTool(name, out var descriptor))
        { throw new InvalidOperationException(name); }
        return descriptor!;
    }
}
