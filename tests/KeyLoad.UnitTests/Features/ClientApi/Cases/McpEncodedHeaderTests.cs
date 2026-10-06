using KeyLoad.Server;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/005/007: the guard matches the actual public SDK header decoding before early reflection.</summary>
internal sealed class McpEncodedHeaderTests
{
    /// <summary>Native encoded names match their decoded body target without changing request bytes.</summary>
    /// <param name="method">The actual native method selecting the target field.</param>
    /// <param name="field">The name or URI field selected by that method.</param>
    [Test]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField)]
    public async Task NativeEncodedNameMatchesDecodedBody(string method, string field)
    {
        var encoded = McpNativeBoundaryTestData.Encode(McpNativeBoundaryTestData.UnicodeName);
        await Assert.That(encoded).IsNotEqualTo(McpNativeBoundaryTestData.UnicodeName);
        await Assert.That(McpHeaderEncoder.DecodeValue(encoded)).IsEqualTo(McpNativeBoundaryTestData.UnicodeName);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(method, encoded), UnitMcpOptions.Execution());
        var wire = McpTransportGuardTestData.Frame(method, new() { [field] = McpNativeBoundaryTestData.UnicodeName });
        var original = wire.ToArray();
        McpTransportGuard.Inspect(wire, headers);
        await Assert.That(headers.Name).IsEqualTo(McpNativeBoundaryTestData.UnicodeName);
        await Assert.That(wire.AsSpan().SequenceEqual(original)).IsTrue();
    }

    /// <summary>A raw-equal encoded header/body pair cannot reach the native decoded mismatch reflection.</summary>
    [Test]
    public async Task RawEqualEncodedPairHasFixedSafeFailure()
    {
        var encoded = McpNativeBoundaryTestData.Encode(McpNativeBoundaryTestData.UnicodeName);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution());
        var wire = McpTransportGuardTestData.Frame(RequestMethods.ToolsCall,
            new() { [McpTransportGuardTestData.NameField] = encoded });
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(wire, headers));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpNativeBoundaryTestData.InvalidTransport);
        await Assert.That(error.Message).DoesNotContain(McpNativeBoundaryTestData.Marker);
        await Assert.That(error.Message).DoesNotContain(encoded);
    }

    /// <summary>Malformed base64 and invalid UTF-8 use fixed errors rather than retaining encoded input.</summary>
    /// <param name="encoded">The genuine invalid encoded native header.</param>
    [Test]
    [Arguments(McpNativeBoundaryTestData.InvalidBase64)]
    [Arguments(McpNativeBoundaryTestData.InvalidUtf8)]
    public async Task InvalidNativeEncodingHasFixedSafeFailure(string encoded)
    {
        await Assert.That(McpHeaderEncoder.DecodeValue(encoded)).IsNull();
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpNativeBoundaryTestData.InvalidTransport);
        await Assert.That(error.Message).DoesNotContain(McpNativeBoundaryTestData.Marker);
        await Assert.That(error.Message).DoesNotContain(encoded);
    }

    /// <summary>Empty decoded targets are rejected even when their encoded field is nonempty.</summary>
    [Test]
    public async Task EmptyDecodedNameHasFixedSafeFailure()
    {
        await Assert.That(McpHeaderEncoder.DecodeValue(McpNativeBoundaryTestData.EmptyEncoded)).IsEqualTo(string.Empty);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: McpNativeBoundaryTestData.EmptyEncoded), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpNativeBoundaryTestData.InvalidTransport);
    }

    /// <summary>A malformed encoded prefix is rejected even when the native decoder leaves it literal.</summary>
    [Test]
    public async Task IncompleteEncodedPrefixHasFixedSafeFailure()
    {
        await Assert.That(McpHeaderEncoder.DecodeValue(McpNativeBoundaryTestData.IncompleteEncoded))
            .IsEqualTo(McpNativeBoundaryTestData.IncompleteEncoded);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.ReadHeaders(
            McpTransportGuardTestData.Headers(name: McpNativeBoundaryTestData.IncompleteEncoded), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpNativeBoundaryTestData.InvalidTransport);
        await Assert.That(error.Message).DoesNotContain(McpNativeBoundaryTestData.Marker);
    }

    /// <summary>Native encoding cannot smuggle decoded forbidden control characters into the checked target.</summary>
    /// <param name="control">The decoded C0 or DEL character forbidden by the checked-name contract.</param>
    [Test]
    [Arguments(McpNativeBoundaryTestData.LineFeed)]
    [Arguments(McpNativeBoundaryTestData.Delete)]
    public async Task DecodedControlCharacterHasFixedSafeFailure(char control)
    {
        var target = McpNativeBoundaryTestData.Marker + control;
        var encoded = McpNativeBoundaryTestData.Encode(target);
        await Assert.That(McpHeaderEncoder.DecodeValue(encoded)).IsEqualTo(target);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpNativeBoundaryTestData.InvalidTransport);
        await Assert.That(error.Message).DoesNotContain(McpNativeBoundaryTestData.Marker);
    }

    /// <summary>Native-encoded horizontal tabs retain the existing checked-name exception for that character.</summary>
    [Test]
    public async Task EncodedHorizontalTabRemainsPermitted()
    {
        var encoded = McpNativeBoundaryTestData.Encode(McpTransportGuardTestData.TabbedName);
        await Assert.That(McpHeaderEncoder.DecodeValue(encoded)).IsEqualTo(McpTransportGuardTestData.TabbedName);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution());
        McpTransportGuard.Inspect(McpTransportGuardTestData.Frame(RequestMethods.ToolsCall,
            new() { [McpTransportGuardTestData.NameField] = McpTransportGuardTestData.TabbedName }), headers);
        await Assert.That(headers.Name).IsEqualTo(McpTransportGuardTestData.TabbedName);
    }

    /// <summary>The encoded length ceiling is enforced before native decoding can reduce the input.</summary>
    [Test]
    public async Task EncodedNameLengthRemainsBounded()
    {
        var exact = McpNativeBoundaryTestData.UnicodeCharacter + new string(McpNativeBoundaryTestData.Padding,
            McpNativeBoundaryTestData.AcceptedPaddingCharacters);
        var encoded = McpNativeBoundaryTestData.Encode(exact);
        await Assert.That(encoded.Length).IsEqualTo(McpNativeBoundaryTestData.AcceptedEncodedCharacters);
        await Assert.That(McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution()).Name).IsEqualTo(exact);
        var over = McpNativeBoundaryTestData.Encode(exact + McpNativeBoundaryTestData.Padding);
        await Assert.That(over.Length).IsEqualTo(McpNativeBoundaryTestData.RejectedEncodedCharacters);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: over), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>Literal catalog names keep their actual native representation and decoded comparison.</summary>
    [Test]
    public async Task LiteralNameRemainsUnchanged()
    {
        var encoded = McpNativeBoundaryTestData.Encode(McpTransportGuardTestData.Target);
        await Assert.That(encoded).IsEqualTo(McpTransportGuardTestData.Target);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution());
        McpTransportGuard.Inspect(McpTransportGuardTestData.Frame(RequestMethods.ToolsCall,
            new() { [McpTransportGuardTestData.NameField] = McpTransportGuardTestData.Target }), headers);
        await Assert.That(headers.Name).IsEqualTo(McpTransportGuardTestData.Target);
    }
}
