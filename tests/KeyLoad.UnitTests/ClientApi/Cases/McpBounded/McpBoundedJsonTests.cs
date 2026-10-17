using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/004/007: native bounded serialization preserves the exact canonical wire contract.</summary>
internal sealed class McpBoundedJsonTests
{
    private const int SpareBytes = 256;
    private const int LargeValueCharacters = 24_576;
    private const int SmallByteLimit = 64;
    private const string EscapedValue = "Привіт é 😀 \"quoted\" \\ \n";
    private const string UnicodeEscape = "\\u";
    private const string SecretMarker = "private-canonical-marker";
    private const string Base64Json = "\"AAEC/w==\"";
    private const string EmptyBufferJson = "\"\"";
    private const string NullJson = "null";

    /// <summary>A real polymorphic command preserves immutable-array contents, optional nulls and defaults.</summary>
    [Test]
    public async Task AcMcp007CommandBytesMatchCanonicalSerializerAtExactBoundary()
    {
        var command = Command(EscapedValue);
        var expected = JsonDefaults.Serialize(command);
        var actual = McpBoundedJson.Serialize(command, expected.Length);

        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        var decoded = JsonDefaults.Deserialize<CommandRequest>(actual);
        await Assert.That(decoded.OwnershipEpoch).IsEqualTo(command.OwnershipEpoch);
        await Assert.That(decoded.Mutations.Length).IsEqualTo(command.Mutations.Length);
        await Assert.That(((PutDocument)decoded.Mutations[0]).ExpectedRevision).IsNull();
        await Assert.That(((PutDocument)decoded.Mutations[0]).Json).IsEqualTo(EscapedValue);
        await Assert.That(Encoding.UTF8.GetString(actual)).Contains(UnicodeEscape);
    }

    /// <summary>The actual strict byte-memory converter writes base64 and accepts an empty initialized buffer.</summary>
    [Test]
    public async Task AcMcp007ByteMemoryUsesTheActualCanonicalBase64Converter()
    {
        ReadOnlyMemory<byte> value = new byte[] { 0, 1, 2, byte.MaxValue };
        await Assert.That(McpBoundedJson.Serialize(value, SmallByteLimit).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(value))).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(McpBoundedJson.Serialize(value, SmallByteLimit))).IsEqualTo(Base64Json);
        await Assert.That(Encoding.UTF8.GetString(McpBoundedJson.Serialize(ReadOnlyMemory<byte>.Empty, SmallByteLimit)))
            .IsEqualTo(EmptyBufferJson);
    }

    /// <summary>Declared strings and undefined numeric enum values retain canonical serializer behavior.</summary>
    [Test]
    public async Task AcMcp007EnumAndResourceDefaultBytesMatchCanonicalSerializer()
    {
        var resource = new ResourceDefinition(McpCanonicalTestData.Resource, ResourceKind.Collection, McpCanonicalTestData.Domain);
        await AssertParity(resource);
        await AssertParity(ResourceKind.Collection);
        await AssertParity((ResourceKind)byte.MaxValue);
        await AssertParity(ImmutableArray<string>.Empty);
    }

    /// <summary>Canonical null is accepted at its exact four-byte limit and remains independently owned.</summary>
    [Test]
    public async Task AcMcp007NullUsesCanonicalBytesAtTheInclusiveLimit()
    {
        var expected = JsonDefaults.Serialize<object?>(null);
        var actual = McpBoundedJson.Serialize<object?>(null, expected.Length);
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(actual)).IsEqualTo(NullJson);
    }

    /// <summary>A payload crossing native serializer buffer boundaries succeeds without changing its bytes.</summary>
    [Test]
    public async Task AcMcp004LargeCanonicalDtoCrossesNativeBuffersWithinItsExactBound()
    {
        var command = Command(new string('x', LargeValueCharacters));
        var expected = JsonDefaults.Serialize(command);
        var actual = McpBoundedJson.Serialize(command, expected.Length);
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
    }

    /// <summary>A single byte below the actual canonical size fails with a fixed safe domain error.</summary>
    [Test]
    public async Task AcMcp004OverrunRejectsCanonicalBytesWithoutReflectingPrivateValues()
    {
        var command = Command(SecretMarker);
        var expected = JsonDefaults.Serialize(command);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpBoundedJson.Serialize(command, expected.Length - 1));
        var largeError = Assert.ThrowsExactly<KeyLoadException>(() => McpBoundedJson.Serialize(
            Command(new string('x', LargeValueCharacters)), SmallByteLimit));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).IsEqualTo(largeError.Message);
        await Assert.That(error.Message).DoesNotContain(SecretMarker);
    }

    /// <summary>Both zero and negative byte ceilings are rejected before serialization.</summary>
    /// <param name="maximumBytes">The invalid configured limit.</param>
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public void AcMcp004ConfiguredByteLimitMustBePositive(int maximumBytes)
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => McpBoundedJson.Serialize(Command(SecretMarker), maximumBytes));

    /// <summary>The actual strict immutable-array failure is preserved instead of being relabeled as admission.</summary>
    [Test]
    public void AcMcp007DefaultImmutableArrayRetainsItsNativeCanonicalFailure()
    {
        var command = new CommandRequest(McpCanonicalTestData.StableId, McpCanonicalTestData.Partition, default);
        Assert.ThrowsExactly<JsonException>(() => McpBoundedJson.Serialize(command, SmallByteLimit));
    }

    private static CommandRequest Command(string value) => new(McpCanonicalTestData.StableId,
        McpCanonicalTestData.Partition, [new PutDocument(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, value)]);

    private static async Task AssertParity<T>(T value)
    {
        var expected = JsonDefaults.Serialize(value);
        var actual = McpBoundedJson.Serialize(value, checked(expected.Length + SpareBytes));
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
    }
}
