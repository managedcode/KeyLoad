using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed class StreamTraversalMcpSchemaTests
{
    private const string Tool = "keyload_streams_read";
    private const string Route = "/streams/read";
    private const string Request = "request";
    private const string UnknownAuthority = "principalId";
    private const string CallerAuthority = "caller-cannot-grant-authority";
    private const string DefinitionsProperty = "$defs";
    private const string PropertiesProperty = "properties";
    private const string DirectionProperty = "direction";
    private const string ExamplesProperty = "examples";
    private const string AnyOfProperty = "anyOf";
    private const string TypeProperty = "type";
    private const string ResultProperty = "result";
    private const string StringType = "string";
    private const string IntegerType = "integer";
    private static readonly string[] ResultProperties = ["stream", "head", "events", "cutPosition", "hasMore", "cursor", "snapshotCutPosition"];
    private static readonly string[] RequestProperties = ["stream", "afterRevision", "limit", "direction", "maxBytes", "cursor"];

    [Test]
    public async Task CompleteOriginalCursorRequestDecodeRefusesCallerAuthorityThenPageRoundtripIsHealthy()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: StreamTraversalTestProtocol.Third, protectedFields: true);
        var first = fixture.Traverse(fixture.Traversal(StreamReadDirection.Backward, limit: StreamTraversalTestProtocol.First));
        var expected = fixture.Traversal(StreamReadDirection.Backward, limit: StreamTraversalTestProtocol.Second,
            maxBytes: int.MaxValue, cursor: first.Cursor);
        await Assert.That(McpOperationCatalog.TryGet(Tool, out var descriptor)).IsTrue();
        var operation = descriptor ?? throw new InvalidOperationException();
        await Assert.That(operation.Route).IsEqualTo(Route);
        await Assert.That(operation.ReadOnly).IsTrue();
        await Assert.That(operation.Idempotent).IsTrue();
        await Assert.That(operation.Destructive).IsFalse();
        var properties = operation.InputSchema.GetProperty(DefinitionsProperty).GetProperty(Request).GetProperty(PropertiesProperty);
        await Assert.That(properties.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(RequestProperties.Order(StringComparer.Ordinal))).IsTrue();
        var directions = properties.GetProperty(DirectionProperty).GetProperty(AnyOfProperty);
        await Assert.That(directions.EnumerateArray().Select(value => value.GetProperty(TypeProperty).GetString())
            .SequenceEqual([StringType, IntegerType])).IsTrue();
        await Assert.That(directions[StreamTraversalTestProtocol.Empty].GetProperty(ExamplesProperty).EnumerateArray()
            .Select(value => value.GetString()).SequenceEqual([nameof(StreamReadDirection.Forward), nameof(StreamReadDirection.Backward)])).IsTrue();
        var resultProperties = operation.OutputSchema.GetProperty(DefinitionsProperty).GetProperty(ResultProperty).GetProperty(PropertiesProperty);
        await Assert.That(resultProperties.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(ResultProperties.Order(StringComparer.Ordinal))).IsTrue();
        var unknown = JsonNode.Parse(JsonDefaults.Serialize(expected))!.AsObject();
        unknown[UnknownAuthority] = CallerAuthority;
        var invalid = Arguments(JsonSerializer.SerializeToUtf8Bytes(unknown));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => operation.Decode(invalid)).Code)
            .IsEqualTo(ErrorCode.Validation);
        var healthyRequest = fixture.Traversal(StreamReadDirection.Backward, limit: StreamTraversalTestProtocol.Second, cursor: first.Cursor);
        var decoded = operation.Decode(Arguments(JsonDefaults.Serialize(healthyRequest)));
        await Assert.That(decoded.ReadKind).IsEqualTo(GrainReadKind.Stream);
        await Assert.That(decoded.CommandKind).IsNull();
        await Assert.That(decoded.CommandId).IsEqualTo(Guid.Empty);
        var runtimeRequest = ((ReadStreamRequest)decoded.Payload);
        await Assert.That(JsonDefaults.Serialize(runtimeRequest).AsSpan().SequenceEqual(JsonDefaults.Serialize(healthyRequest))).IsTrue();
        var result = fixture.Traverse(runtimeRequest);
        await StreamTraversalAssertions.PageAsync(result, [StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.First],
            StreamTraversalTestProtocol.Third, false);
        var encoded = OrleansSerialization.Serialize(result);
        var runtimeResult = OrleansSerialization.Deserialize<StreamPage>(encoded);
        var publicResult = JsonSerializer.Deserialize<StreamPage>(JsonDefaults.Serialize(runtimeResult), JsonDefaults.Options);
        await Assert.That(JsonDefaults.Serialize(publicResult).AsSpan().SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
        await Assert.That(OrleansSerialization.Measure(result)).IsEqualTo((long)encoded.Length);
    }

    private static Dictionary<string, JsonElement> Arguments(byte[] bytes)
    {
        using var body = JsonDocument.Parse(bytes);
        return new(StringComparer.Ordinal)
        {
            [Request] = body.RootElement.Clone()
        };
    }
}
