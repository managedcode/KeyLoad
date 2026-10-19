using KeyLoad.Server;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/005/007: the guard matches the actual public SDK header decoding before early reflection.</summary>
internal sealed class McpEncodedHeaderTests
{
    /// <summary>Execution encoded names match their decoded body target without changing request bytes.</summary>
    /// <param name="method">The actual execution method selecting the target field.</param>
    /// <param name="field">The name or URI field selected by that method.</param>
    [Test]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField)]
    public async Task EncodedNameMatchesDecodedBody(string method, string field)
    {
        var encoded = McpBoundaryTestData.Encode(McpBoundaryTestData.UnicodeName);
        await Assert.That(encoded).IsNotEqualTo(McpBoundaryTestData.UnicodeName);
        await Assert.That(McpHeaderEncoder.DecodeValue(encoded)).IsEqualTo(McpBoundaryTestData.UnicodeName);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(method, encoded), UnitMcpOptions.Execution());
        var wire = McpTransportGuardTestData.Frame(method, new()
        {
            [field] = McpBoundaryTestData.UnicodeName
        });
        var original = wire.ToArray();
        McpTransportGuard.Inspect(wire, headers);
        await Assert.That(headers.Name).IsEqualTo(McpBoundaryTestData.UnicodeName);
        await Assert.That(wire.AsSpan().SequenceEqual(original)).IsTrue();
    }

    /// <summary>A raw-equal encoded header/body pair cannot reach the execution decoded mismatch reflection.</summary>
    [Test]
    public async Task RawEqualEncodedPairHasFixedSafeFailure()
    {
        var encoded = McpBoundaryTestData.Encode(McpBoundaryTestData.UnicodeName);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution());
        var wire = McpTransportGuardTestData.Frame(RequestMethods.ToolsCall,
            new()
            {
                [McpTransportGuardTestData.NameField] = encoded
            });
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(wire, headers));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpBoundaryTestData.InvalidTransport);
        await Assert.That(error.Message).DoesNotContain(McpBoundaryTestData.Marker);
        await Assert.That(error.Message).DoesNotContain(encoded);
    }

    /// <summary>Malformed base64 and invalid UTF-8 use fixed errors rather than retaining encoded input.</summary>
    /// <param name="encoded">The genuine invalid encoded execution header.</param>
    [Test]
    [Arguments(McpBoundaryTestData.InvalidBase64)]
    [Arguments(McpBoundaryTestData.InvalidUtf8)]
    public async Task InvalidEncodingHasFixedSafeFailure(string encoded)
    {
        await Assert.That(McpHeaderEncoder.DecodeValue(encoded)).IsNull();
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpBoundaryTestData.InvalidTransport);
        await Assert.That(error.Message).DoesNotContain(McpBoundaryTestData.Marker);
        await Assert.That(error.Message).DoesNotContain(encoded);
    }

    /// <summary>Empty decoded targets are rejected even when their encoded field is nonempty.</summary>
    [Test]
    public async Task EmptyDecodedNameHasFixedSafeFailure()
    {
        await Assert.That(McpHeaderEncoder.DecodeValue(McpBoundaryTestData.EmptyEncoded)).IsEqualTo(string.Empty);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: McpBoundaryTestData.EmptyEncoded), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpBoundaryTestData.InvalidTransport);
    }

    /// <summary>A malformed encoded prefix is rejected even when the execution decoder leaves it literal.</summary>
    [Test]
    public async Task IncompleteEncodedPrefixHasFixedSafeFailure()
    {
        await Assert.That(McpHeaderEncoder.DecodeValue(McpBoundaryTestData.IncompleteEncoded))
            .IsEqualTo(McpBoundaryTestData.IncompleteEncoded);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.ReadHeaders(
            McpTransportGuardTestData.Headers(name: McpBoundaryTestData.IncompleteEncoded), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpBoundaryTestData.InvalidTransport);
        await Assert.That(error.Message).DoesNotContain(McpBoundaryTestData.Marker);
    }

    /// <summary>Execution encoding cannot smuggle decoded forbidden control characters into the checked target.</summary>
    /// <param name="control">The decoded C0 or DEL character forbidden by the checked-name contract.</param>
    [Test]
    [Arguments(McpBoundaryTestData.LineFeed)]
    [Arguments(McpBoundaryTestData.Delete)]
    public async Task DecodedControlCharacterHasFixedSafeFailure(char control)
    {
        var target = McpBoundaryTestData.Marker + control;
        var encoded = McpBoundaryTestData.Encode(target);
        await Assert.That(McpHeaderEncoder.DecodeValue(encoded)).IsEqualTo(target);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpBoundaryTestData.InvalidTransport);
        await Assert.That(error.Message).DoesNotContain(McpBoundaryTestData.Marker);
    }

    /// <summary>Execution-encoded horizontal tabs retain the existing checked-name exception for that character.</summary>
    [Test]
    public async Task EncodedHorizontalTabRemainsPermitted()
    {
        var encoded = McpBoundaryTestData.Encode(McpTransportGuardTestData.TabbedName);
        await Assert.That(McpHeaderEncoder.DecodeValue(encoded)).IsEqualTo(McpTransportGuardTestData.TabbedName);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution());
        McpTransportGuard.Inspect(McpTransportGuardTestData.Frame(RequestMethods.ToolsCall,
            new()
            {
                [McpTransportGuardTestData.NameField] = McpTransportGuardTestData.TabbedName
            }), headers);
        await Assert.That(headers.Name).IsEqualTo(McpTransportGuardTestData.TabbedName);
    }

    /// <summary>The encoded length ceiling is enforced before execution decoding can reduce the input.</summary>
    [Test]
    public async Task EncodedNameLengthRemainsBounded()
    {
        var exact = McpBoundaryTestData.UnicodeCharacter + new string(McpBoundaryTestData.Padding,
            McpBoundaryTestData.AcceptedPaddingCharacters);
        var encoded = McpBoundaryTestData.Encode(exact);
        await Assert.That(encoded.Length).IsEqualTo(McpBoundaryTestData.AcceptedEncodedCharacters);
        await Assert.That(McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution()).Name).IsEqualTo(exact);
        var over = McpBoundaryTestData.Encode(exact + McpBoundaryTestData.Padding);
        await Assert.That(over.Length).IsEqualTo(McpBoundaryTestData.RejectedEncodedCharacters);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: over), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>Literal catalog names keep their actual execution representation and decoded comparison.</summary>
    [Test]
    public async Task LiteralNameRemainsUnchanged()
    {
        var encoded = McpBoundaryTestData.Encode(McpTransportGuardTestData.Target);
        await Assert.That(encoded).IsEqualTo(McpTransportGuardTestData.Target);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(name: encoded), UnitMcpOptions.Execution());
        McpTransportGuard.Inspect(McpTransportGuardTestData.Frame(RequestMethods.ToolsCall,
            new()
            {
                [McpTransportGuardTestData.NameField] = McpTransportGuardTestData.Target
            }), headers);
        await Assert.That(headers.Name).IsEqualTo(McpTransportGuardTestData.Target);
    }
}
