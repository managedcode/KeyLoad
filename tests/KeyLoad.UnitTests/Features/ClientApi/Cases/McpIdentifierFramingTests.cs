using System.Text;
using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/005/007: the reflected root identifier is bounded by its actual encoded UTF-8 token.</summary>
internal sealed class McpIdentifierFramingTests
{
    private const int MaximumWireBytes = 65_536;
    private const int MaximumEncodedIdentifierBytes = 256;
    private const int UnicodeDecodedBytes = 88;
    private const int QuoteDecodedBytes = 128;
    private const string IdentifierField = "id";
    private const string ParametersField = "params";
    private const string ScalarIdentifierJson = "{\"id\":7}";
    private const string NullIdentifierJson = "{\"id\":null}";

    /// <summary>Ordinary UTF-8, Unicode escapes and quote escapes accept 256 encoded bytes and reject 257.</summary>
    /// <param name="encoding">The genuine JSON string spelling used for the identifier.</param>
    /// <param name="decodedBytes">The independently expected decoded UTF-8 length.</param>
    [Test]
    [Arguments(McpIdentifierEncoding.Ascii, MaximumEncodedIdentifierBytes)]
    [Arguments(McpIdentifierEncoding.Utf8, MaximumEncodedIdentifierBytes)]
    [Arguments(McpIdentifierEncoding.UnicodeEscape, UnicodeDecodedBytes)]
    [Arguments(McpIdentifierEncoding.QuoteEscape, QuoteDecodedBytes)]
    public async Task RootIdentifierHasExactEncodedByteBoundary(McpIdentifierEncoding encoding, int decodedBytes)
    {
        var encoded = McpResponseBoundaryTestData.EncodedIdentifier(encoding, MaximumEncodedIdentifierBytes);
        await Assert.That(Encoding.UTF8.GetByteCount(encoded)).IsEqualTo(MaximumEncodedIdentifierBytes);
        var wire = McpResponseBoundaryTestData.IdentifierFrame(encoded);
        var shape = McpFrameBounds.Inspect(wire, MaximumWireBytes);
        using var document = JsonDocument.Parse(wire);
        await Assert.That(shape.PropertyCount).IsEqualTo(1);
        var decoded = document.RootElement.GetProperty(IdentifierField).GetString();
        await Assert.That(decoded).IsNotNull();
        await Assert.That(Encoding.UTF8.GetByteCount(decoded!)).IsEqualTo(decodedBytes);
        var over = McpResponseBoundaryTestData.IdentifierFrame(encoded + McpResponseBoundaryTestData.Padding);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpFrameBounds.Inspect(over, MaximumWireBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Escaped property spelling cannot bypass the root identifier byte ceiling.</summary>
    [Test]
    public async Task EscapedRootIdentifierKeyKeepsTheSameBound()
    {
        var encoded = McpResponseBoundaryTestData.EncodedIdentifier(McpIdentifierEncoding.Ascii, MaximumEncodedIdentifierBytes);
        var exact = McpFrameBounds.Inspect(McpResponseBoundaryTestData.IdentifierFrame(encoded, escapedKey: true), MaximumWireBytes);
        await Assert.That(exact.PropertyCount).IsEqualTo(1);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpFrameBounds.Inspect(
            McpResponseBoundaryTestData.IdentifierFrame(encoded + McpResponseBoundaryTestData.Padding, escapedKey: true), MaximumWireBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Nested business identifiers are preserved even when longer than the protocol identifier ceiling.</summary>
    [Test]
    public async Task NestedBusinessIdentifierRemainsUntouched()
    {
        var value = new string(McpResponseBoundaryTestData.Padding, MaximumEncodedIdentifierBytes + 1);
        var wire = JsonDefaults.Serialize(new Dictionary<string, object?>
        {
            [ParametersField] = new Dictionary<string, object?> { [IdentifierField] = value }
        });
        var before = wire.ToArray();
        var shape = McpFrameBounds.Inspect(wire, MaximumWireBytes);
        using var document = JsonDocument.Parse(wire);
        await Assert.That(shape.PropertyCount).IsEqualTo(2);
        await Assert.That(wire.AsSpan().SequenceEqual(before)).IsTrue();
        await Assert.That(document.RootElement.GetProperty(ParametersField).GetProperty(IdentifierField).GetString()).IsEqualTo(value);
    }

    /// <summary>Numeric and null identifier semantics remain with the native SDK.</summary>
    /// <param name="json">The genuine scalar identifier frame.</param>
    [Test]
    [Arguments(ScalarIdentifierJson)]
    [Arguments(NullIdentifierJson)]
    public async Task ScalarIdentifierSemanticsRemainNative(string json)
    {
        var shape = McpFrameBounds.Inspect(Encoding.UTF8.GetBytes(json), MaximumWireBytes);
        await Assert.That(shape.PropertyCount).IsEqualTo(1);
    }

    /// <summary>A root business identifier in a canonical reply does not acquire the inbound protocol ID limit.</summary>
    [Test]
    public async Task ReplyIdentifierIsNotAnInboundProtocolIdentifier()
    {
        var encoded = McpResponseBoundaryTestData.EncodedIdentifier(McpIdentifierEncoding.Ascii, MaximumEncodedIdentifierBytes + 1);
        var shape = McpFrameBounds.InspectReply(McpResponseBoundaryTestData.IdentifierFrame(encoded), MaximumWireBytes);
        await Assert.That(shape.PropertyCount).IsEqualTo(1);
        await Assert.That(shape.Depth).IsEqualTo(1);
    }
}
