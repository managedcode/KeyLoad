using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/005/007: actual polymorphic SDK messages are bounded before SSE serialization.</summary>
internal sealed class McpNativeOutputTests
{
    /// <summary>Success and failure native messages include Unicode metadata and accept their exact serialized byte count.</summary>
    /// <param name="failure">Whether to serialize an actual failure tool result.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualNativeToolMessageHasExactByteBoundary(bool failure)
    {
        using var owner = failure
            ? McpReplyOwner.Failure(ErrorCode.PermissionDenied, McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes)
            : McpReplyOwner.Success(JsonDefaults.Serialize(McpOutputTestData.Marker), McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution());
        JsonRpcMessage message = McpOutputTestData.Response(owner.ToolResult());
        var encoded = JsonSerializer.SerializeToUtf8Bytes(message, McpJsonUtilities.DefaultOptions);
        McpNativeOutput.Validate(message, encoded.Length, UnitMcpOptions.Execution());
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpNativeOutput.Validate(message, encoded.Length - 1, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).DoesNotContain(McpOutputTestData.Marker);
        using var stream = new McpBoundedWriteStream(encoded.Length);
        JsonSerializer.Serialize(stream, message, McpJsonUtilities.DefaultOptions);
        await Assert.That(stream.WrittenBytes).IsEqualTo(encoded.Length);
        await Assert.That(stream.BorrowBuffer().Span[..stream.WrittenBytes].SequenceEqual(encoded)).IsTrue();
    }

    /// <summary>Genuine native protocol errors use the same complete-message byte bound.</summary>
    [Test]
    public async Task ActualNativeProtocolErrorHasExactByteBoundary()
    {
        JsonRpcMessage message = new JsonRpcError
        {
            Id = new RequestId(McpOutputTestData.NativeIdentifier),
            Error = new JsonRpcErrorDetail { Code = (int)McpErrorCode.InvalidParams, Message = McpOutputTestData.SafeProtocolError }
        };
        var encoded = JsonSerializer.SerializeToUtf8Bytes(message, McpJsonUtilities.DefaultOptions);
        McpNativeOutput.Validate(message, encoded.Length, UnitMcpOptions.Execution());
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpNativeOutput.Validate(message, encoded.Length - 1, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Native depth 64 succeeds while the next nested container receives a safe resource failure.</summary>
    [Test]
    public async Task CompleteNativeMessageRetainsDepth64()
    {
        JsonRpcMessage exact = new JsonRpcResponse
        {
            Id = new RequestId(McpOutputTestData.NativeIdentifier),
            Result = JsonNode.Parse(McpResponseBoundaryTestData.NestedArrays(McpOutputTestData.NativeResultDepth))
        };
        McpNativeOutput.Validate(exact, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution());
        JsonRpcMessage over = new JsonRpcResponse
        {
            Id = new RequestId(McpOutputTestData.NativeIdentifier),
            Result = JsonNode.Parse(McpResponseBoundaryTestData.NestedArrays(McpOutputTestData.NativeResultDepth + 1))
        };
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpNativeOutput.Validate(over, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).DoesNotContain(McpOutputTestData.Marker);
    }

    /// <summary>Output business identifiers do not inherit the inbound JSON-RPC ID resource limit.</summary>
    [Test]
    public async Task OutputBusinessIdentifierIsNotAnInboundIdentifier()
    {
        var encoded = McpResponseBoundaryTestData.IdentifierFrame(new string(McpResponseBoundaryTestData.Padding, McpOutputTestData.LongBusinessIdentifierBytes));
        JsonRpcMessage message = new JsonRpcResponse
        {
            Id = new RequestId(McpOutputTestData.NativeIdentifier),
            Result = JsonNode.Parse(encoded)
        };
        McpNativeOutput.Validate(message, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution());
        await Assert.That(((JsonRpcResponse)message).Result![McpOutputTestData.IdentifierField]!.GetValue<string>().Length)
            .IsEqualTo(McpOutputTestData.LongBusinessIdentifierBytes);
    }
}
