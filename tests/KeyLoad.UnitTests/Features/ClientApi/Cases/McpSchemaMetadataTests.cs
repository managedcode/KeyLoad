using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003: real metadata, strict collections and actual base64 converters.</summary>
internal sealed class McpSchemaMetadataTests
{
    private const string WireName = "wireName";
    private const string Attempts = "attempts";
    private const string Note = "note";
    private const string Labels = "labels";
    private const string Key = "key";
    private const string Value = "value";
    private const string EncodingKey = "contentEncoding";
    private const string Base64 = "base64";
    private const string ValidBytes = "{\"key\":\"AQI=\",\"value\":\"AwQ=\"}";
    private const string NullBytes = "{\"key\":null,\"value\":\"AwQ=\"}";
    private const string InvalidBytes = "{\"key\":\"not-base64!\",\"value\":\"AwQ=\"}";
    private const string NullLabels = "{\"wireName\":\"record\",\"labels\":null}";
    private const string ValidLabels = "{\"wireName\":\"record\",\"labels\":[]}";
    private const int DefaultAttempts = 3;
    private const int FirstPropertyOrder = -1;
    private const int RemovedConverterCount = 2;

    private sealed record MetadataProbe(
        [property: JsonPropertyName(WireName), JsonPropertyOrder(FirstPropertyOrder)] string Name,
        ImmutableArray<string> Labels, int Attempts = DefaultAttempts, string? Note = null);

    /// <summary>Actual attributed record metadata retains property order, required fields and optional defaults.</summary>
    [Test]
    public async Task AcMcp003ProjectionKeepsCanonicalNamesOrderingRequiredAndDefaults()
    {
        var schema = McpSchemaInspector.DefinedRequest(McpSchemaFactory.CreateInput(typeof(MetadataProbe), false));
        var properties = schema.GetProperty(McpSchemaInspector.Properties);
        await Assert.That(properties.EnumerateObject().First().Name).IsEqualTo(WireName);
        await Assert.That(properties.GetProperty(Attempts).GetProperty(McpSchemaInspector.Default).GetInt32()).IsEqualTo(DefaultAttempts);
        await Assert.That(properties.GetProperty(Note).GetProperty(McpSchemaInspector.Default).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(McpSchemaInspector.HasType(properties.GetProperty(Note), McpSchemaInspector.Null)).IsTrue();
        await Assert.That(schema.GetProperty(McpSchemaInspector.Required).EnumerateArray()
                .Select(item => item.GetString() ?? throw new InvalidOperationException()))
            .IsEquivalentTo(new[] { WireName, Labels });
        await Assert.That(McpSchemaInspector.HasType(properties.GetProperty(Labels), McpSchemaInspector.Array)).IsTrue();
        await Assert.That(McpSchemaInspector.HasType(properties.GetProperty(Labels), McpSchemaInspector.Null)).IsFalse();
    }

    /// <summary>Schema export preserves runtime strict array behavior and canonical converter registrations.</summary>
    [Test]
    public async Task AcMcp003SchemaCopyDoesNotReplaceCanonicalStrictConverters()
    {
        var before = JsonDefaults.Options.Converters.ToArray();
        var projection = McpSchemaProjection.Create();
        await Assert.That(projection.IsReadOnly).IsTrue();
        await Assert.That(projection.Converters.Count).IsEqualTo(before.Length - RemovedConverterCount);
        await Assert.That(JsonDefaults.Options.Converters.ToArray()).IsEquivalentTo(before);
        await Assert.That(projection.RespectNullableAnnotations).IsEqualTo(JsonDefaults.Options.RespectNullableAnnotations);
        await Assert.That(projection.RespectRequiredConstructorParameters).IsEqualTo(JsonDefaults.Options.RespectRequiredConstructorParameters);
        await Assert.That(projection.UnmappedMemberHandling).IsEqualTo(JsonDefaults.Options.UnmappedMemberHandling);
        await Assert.That(Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<MetadataProbe>(NullLabels, JsonDefaults.Options))).IsNotNull();
        await Assert.That(JsonSerializer.Deserialize<MetadataProbe>(ValidLabels, JsonDefaults.Options)!.Labels.IsEmpty).IsTrue();
        await Assert.That(Assert.ThrowsExactly<JsonException>(() =>
            JsonDefaults.Serialize(new MetadataProbe(McpCanonicalTestData.Entity, default)))).IsNotNull();
    }

    /// <summary>Native string schemas correspond to canonical base64 bytes and runtime rejection of null or malformed bytes.</summary>
    [Test]
    public async Task AcMcp003ByteMemorySchemasDescribeActualStrictBase64Wire()
    {
        var schema = McpSchemaInspector.DefinedRequest(McpSchemaFactory.CreateInput(typeof(KeyValueRecord), false));
        foreach (var name in new[] { Key, Value })
        {
            var field = schema.GetProperty(McpSchemaInspector.Properties).GetProperty(name);
            await Assert.That(McpSchemaInspector.HasType(field, McpSchemaInspector.String)).IsTrue();
            await Assert.That(McpSchemaInspector.HasType(field, McpSchemaInspector.Null)).IsFalse();
            await Assert.That(field.GetProperty(EncodingKey).GetString()).IsEqualTo(Base64);
        }
        var record = JsonSerializer.Deserialize<KeyValueRecord>(ValidBytes, JsonDefaults.Options)!;
        await Assert.That(JsonSerializer.Serialize(record, JsonDefaults.Options)).IsEqualTo(ValidBytes);
        await Assert.That(Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<KeyValueRecord>(NullBytes, JsonDefaults.Options))).IsNotNull();
        await Assert.That(Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<KeyValueRecord>(InvalidBytes, JsonDefaults.Options))).IsNotNull();
    }

    /// <summary>The actual owning Problem converter fails closed when requested as an unsupported input schema.</summary>
    [Test]
    public async Task AcMcp003ActualUnprojectedProblemConverterCannotBecomeAnOpaqueSchema()
    {
        await Assert.That(Assert.ThrowsExactly<InvalidOperationException>(() =>
            McpSchemaFactory.CreateInput(typeof(ManagedCode.Communication.Problem), false))).IsNotNull();
    }
}
