using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;
using KeyLoad.UnitTests.Features.QueryExecution;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

/// <summary>Required supporting schema/native decoder controls; database flows are classified separately.</summary>
internal sealed class FollowerDocumentSchemaTests
{
    private const string Tool = "keyload_documents_read_follower";
    private const string Route = "/v1/documents/read-follower";
    private const string Closed = "additionalProperties";
    private const string DocumentField = "document";
    private const string HiddenProof = "credential";
    private const string PrivateMarker = "private-proof-input-sentinel";

    [Test]
    public async Task CanonicalSdkMcpAndSqlPayloadShareFullTypedRequestAndHideServerOnlyProof()
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request(minimum: fixture.Capture(fixture.Request()).Token);
        var descriptor = Find();
        await Assert.That(descriptor.Route).IsEqualTo(Route);
        await Assert.That(descriptor.ReadKind).IsEqualTo(GrainReadKind.FollowerDocument);
        await Assert.That(descriptor.CommandKind).IsNull();
        await Assert.That(descriptor.ReadOnly).IsTrue();
        await Assert.That(descriptor.Idempotent).IsTrue();
        await Assert.That(descriptor.Destructive).IsFalse();
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options) };
        var decoded = descriptor.Decode(arguments);
        var typed = NativeSerialization.Deserialize<ReadFollowerDocumentRequestV1>(decoded.Payload.Span)!;
        await Assert.That(JsonDefaults.Serialize(typed).SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        await Assert.That(decoded.CommandId).IsEqualTo(Guid.Empty);
        var sql = new SqlOperationRequest(fixture.Db.Partition, SqlOperationTestData.CallPrefix + Tool + SqlOperationTestData.CallSuffix,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            { [SqlOperationTestData.Parameter] = JsonSerializer.SerializeToElement(arguments, JsonDefaults.Options) });
        var compiled = SqlOperationTestData.Compile(sql);
        await Assert.That(compiled.ReadKind).IsEqualTo(GrainReadKind.FollowerDocument);
        await Assert.That(compiled.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(JsonDefaults.Serialize(NativeSerialization.Deserialize<ReadFollowerDocumentRequestV1>(compiled.Payload.Span)!)
            .SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        var untrusted = JsonNode.Parse(arguments[McpCanonicalTestData.RequestKey].GetRawText())!.AsObject();
        untrusted[HiddenProof] = PrivateMarker;
        arguments[McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(untrusted, JsonDefaults.Options);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => descriptor.Decode(arguments));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(rejected.Message.Contains(PrivateMarker, StringComparison.Ordinal)).IsFalse();
        var native = fixture.Complete(request, fixture.Capture(request));
        var decodedResult = NativeSerialization.Deserialize<FollowerDocumentReadResultV1>(NativeSerialization.Serialize(native))!;
        await FollowerDocumentAssertions.FullAsync(fixture, decodedResult, fixture.Position, fixture.Position,
            FollowerDocumentFixture.FirstRevision, FollowerDocumentFixture.OriginalJson, fixture.Principal);
    }

    [Test]
    public async Task ActualGeneratedClosedSchemasRequireExplicitFollowerAndLagAndDescribeSeparateCuts()
    {
        var descriptor = Find();
        var input = McpSchemaInspector.DefinedRequest(descriptor.InputSchema);
        await ShapeAsync(input, ["version", "reference", "replicaId", "maximumLagPositions", "minimumToken"],
            ["version", "reference", "replicaId", "maximumLagPositions"]);
        var root = descriptor.OutputSchema;
        var result = root.GetProperty(McpSchemaInspector.OneOf)[0].GetProperty(McpSchemaInspector.Properties)
            .GetProperty(McpSchemaInspector.Result);
        if (result.TryGetProperty(McpSchemaInspector.Ref, out var reference))
        { result = McpSchemaInspector.Resolve(root, reference.GetString()!); }
        string[] fields = ["version", "mode", "replicaId", "capturedTerm", "authorityTerm", "dataToken",
            "authorizationToken", "positionLag", "policyEpoch", DocumentField];
        await ShapeAsync(result, fields, fields);
        await Assert.That(McpSchemaInspector.HasType(result, McpSchemaInspector.Null)).IsFalse();
        var document = result.GetProperty(McpSchemaInspector.Properties).GetProperty(DocumentField);
        if (document.TryGetProperty(McpSchemaInspector.Ref, out var documentReference))
        { document = McpSchemaInspector.Resolve(root, documentReference.GetString()!); }
        await Assert.That(McpSchemaInspector.HasType(document, McpSchemaInspector.Null)).IsTrue();
        foreach (var schema in new[] { descriptor.InputSchema, root })
        {
            var json = schema.GetRawText();
            await Assert.That(json.Contains("credentialId", StringComparison.Ordinal)).IsFalse();
            await Assert.That(json.Contains("digest", StringComparison.Ordinal)).IsFalse();
            await Assert.That(json.Contains("readGeneration", StringComparison.Ordinal)).IsFalse();
        }
    }

    private static McpOperationDescriptor Find() => McpOperationCatalog.TryGetTool(Tool, out var descriptor)
        ? descriptor : throw new InvalidOperationException("The explicit follower tool is unavailable.");

    private static async Task ShapeAsync(JsonElement schema, string[] fields, string[] required)
    {
        await Assert.That(schema.GetProperty(Closed).GetBoolean()).IsFalse();
        var names = schema.GetProperty(McpSchemaInspector.Properties).EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(names.SetEquals(fields)).IsTrue();
        var mandatory = schema.GetProperty(McpSchemaInspector.Required).EnumerateArray()
            .Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
        await Assert.That(mandatory.SetEquals(required)).IsTrue();
    }
}
