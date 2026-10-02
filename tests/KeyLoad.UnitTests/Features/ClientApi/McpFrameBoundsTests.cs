using System.Text;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/004/005: framing is strict, bounded, and rejects hostile wire payloads before SDK parsing.</summary>
internal sealed class McpFrameBoundsTests
{
    private const int MaximumWireBytes = 1_000_000;
    private const int MaximumDepth = 64;
    private const int MaximumTokens = 131_072;
    private const int MaximumProperties = 32_768;
    private const int MaximumPropertyNameBytes = 256;

    /// <summary>Empty and ordinary object frames report actual JSON tokens and property counts.</summary>
    [Test]
    public async Task InspectReportsObjectShape()
    {
        var empty = McpFrameBounds.Inspect(Encoding.UTF8.GetBytes("{}"), MaximumWireBytes);
        await Assert.That(empty.TokenCount).IsEqualTo(2);
        await Assert.That(empty.PropertyCount).IsEqualTo(0);

        var ordinary = McpFrameBounds.Inspect(Encoding.UTF8.GetBytes("{\"name\":\"é\"}"), MaximumWireBytes);
        await Assert.That(ordinary.TokenCount).IsEqualTo(4);
        await Assert.That(ordinary.PropertyCount).IsEqualTo(1);
    }

    /// <summary>Names may recur in separate nested objects, while escaped spellings of one name are duplicates.</summary>
    [Test]
    public async Task DuplicateNamesAreScopedToTheirObjectAndComparedAfterDecoding()
    {
        var nested = McpFrameBounds.Inspect(Encoding.UTF8.GetBytes("{\"x\":1,\"child\":{\"x\":2}}"), MaximumWireBytes);
        await Assert.That(nested.PropertyCount).IsEqualTo(3);

        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.Inspect(Encoding.UTF8.GetBytes("{\"x\":1,\"\\u0078\":2}"), MaximumWireBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>Only a single JSON object is accepted as an MCP frame root.</summary>
    /// <param name="frame">The candidate JSON frame.</param>
    [Test]
    [Arguments("[]")]
    [Arguments("\"text\"")]
    [Arguments("7")]
    [Arguments("null")]
    [Arguments("")]
    [Arguments("{} {}")]
    public async Task NonObjectOrMultipleRootsAreRejected(string frame)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.Inspect(Encoding.UTF8.GetBytes(frame), MaximumWireBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>Malformed JSON and invalid UTF-8 share a fixed safe validation detail without reflecting attacker text.</summary>
    [Test]
    public async Task InvalidJsonAndUtf8UseFixedSafeDetail()
    {
        var malformed = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.Inspect(Encoding.UTF8.GetBytes("{\"secret-marker\":]"), MaximumWireBytes));
        var invalidUtf8 = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.Inspect([0x7B, 0x22, 0x78, 0x22, 0x3A, 0x22, 0xC3, 0x28, 0x22, 0x7D], MaximumWireBytes));

        await Assert.That(malformed.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(invalidUtf8.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(malformed.Message).IsEqualTo(invalidUtf8.Message);
        await Assert.That(malformed.Message).DoesNotContain("secret-marker");
    }

    /// <summary>The exact UTF-8 wire-byte ceiling is accepted and one byte below the frame size is exhausted.</summary>
    [Test]
    public async Task WireByteLimitIncludesMultibyteUtf8AndAcceptsExactBoundary()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"value\":\"é\"}");
        var shape = McpFrameBounds.Inspect(bytes, bytes.Length);
        await Assert.That(shape.PropertyCount).IsEqualTo(1);

        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpFrameBounds.Inspect(bytes, bytes.Length - 1));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Container nesting at the configured ceiling succeeds; the next nested level is exhausted.</summary>
    [Test]
    public async Task NestingDepthHasAnExactBoundary()
    {
        var exact = McpFrameBounds.Inspect(NestedFrame(MaximumDepth), MaximumWireBytes);
        await Assert.That(exact.PropertyCount).IsEqualTo(1);

        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.Inspect(NestedFrame(MaximumDepth + 1), MaximumWireBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Token capacity accepts its exact boundary and rejects one additional scalar token.</summary>
    [Test]
    public async Task TokenCountHasAnExactBoundary()
    {
        var exact = McpFrameBounds.Inspect(ArrayFrame(MaximumTokens - 5), MaximumWireBytes);
        await Assert.That(exact.TokenCount).IsEqualTo(MaximumTokens);

        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.Inspect(ArrayFrame(MaximumTokens - 4), MaximumWireBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Property capacity accepts its exact boundary and rejects one additional property.</summary>
    [Test]
    public async Task PropertyCountHasAnExactBoundary()
    {
        var exact = McpFrameBounds.Inspect(PropertyFrame(MaximumProperties), MaximumWireBytes);
        await Assert.That(exact.PropertyCount).IsEqualTo(MaximumProperties);

        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.Inspect(PropertyFrame(MaximumProperties + 1), MaximumWireBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Property-name capacity is measured in encoded UTF-8 bytes, including the exact permitted size.</summary>
    [Test]
    public async Task EncodedPropertyNameHasAnExactByteBoundary()
    {
        var exactName = new string('é', MaximumPropertyNameBytes / 2);
        var overName = new string('a', MaximumPropertyNameBytes - 1) + 'é';
        var exact = McpFrameBounds.Inspect(PropertyNameFrame(exactName), MaximumWireBytes);
        await Assert.That(exact.PropertyCount).IsEqualTo(1);

        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.Inspect(PropertyNameFrame(overName), MaximumWireBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    private static byte[] NestedFrame(int depth)
    {
        var json = new StringBuilder("{\"v\":");
        json.Append('[', depth - 1).Append('0').Append(']', depth - 1).Append('}');
        return Encoding.UTF8.GetBytes(json.ToString());
    }

    private static byte[] ArrayFrame(int scalarCount)
    {
        var json = new StringBuilder("{\"items\":[");
        for (var index = 0; index < scalarCount; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append('0');
        }

        return Encoding.UTF8.GetBytes(json.Append("]}").ToString());
    }

    private static byte[] PropertyFrame(int propertyCount)
    {
        var json = new StringBuilder("{");
        for (var index = 0; index < propertyCount; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append("\"p").Append(index).Append("\":0");
        }

        return Encoding.UTF8.GetBytes(json.Append('}').ToString());
    }

    private static byte[] PropertyNameFrame(string name)
    {
        return Encoding.UTF8.GetBytes($"{{\"{name}\":0}}");
    }
}
