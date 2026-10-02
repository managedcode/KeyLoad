using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/005/007: a fresh native fallback retains injected metadata and exact message identity.</summary>
internal sealed class McpResponseReplacementTests
{
    /// <summary>Replacement moves the original metadata node without copying old business payload or changing native identity.</summary>
    /// <param name="failure">Whether to replace with the genuine safe failure rather than success tool result.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReplacementPreservesNativeMetadataAndIdentity(bool failure)
    {
        var metadata = McpNativeBoundaryTestData.Metadata();
        var originalMetadata = JsonSerializer.SerializeToUtf8Bytes(metadata, McpJsonUtilities.DefaultOptions);
        var oldResult = new JsonObject
        {
            [McpNativeBoundaryTestData.OldPayloadField] = McpNativeBoundaryTestData.OldPayload,
            [McpNativeBoundaryTestData.MetaField] = metadata
        };
        var context = new JsonRpcMessageContext();
        var identifier = new RequestId(McpOutputTestData.NativeIdentifier);
        var response = new JsonRpcResponse { Id = identifier, Context = context, Result = oldResult };
        using var owner = McpNativeBoundaryTestData.Reply(failure);
        var tool = owner.ToolResult();
        var expected = JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)
            ?? throw new InvalidOperationException(McpNativeBoundaryTestData.MissingNode);
        McpResponseReplacement.Apply(response, tool);
        var replaced = response.Result as JsonObject
            ?? throw new InvalidOperationException(McpNativeBoundaryTestData.MissingNode);
        await Assert.That(replaced).IsNotSameReferenceAs(oldResult);
        await Assert.That(replaced[McpNativeBoundaryTestData.MetaField]).IsSameReferenceAs(metadata);
        await Assert.That(metadata.Parent).IsSameReferenceAs(replaced);
        await Assert.That(oldResult[McpNativeBoundaryTestData.MetaField]).IsNull();
        await Assert.That(response.Context).IsSameReferenceAs(context);
        await Assert.That(response.Id).IsEqualTo(identifier);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(metadata, McpJsonUtilities.DefaultOptions)
            .AsSpan().SequenceEqual(originalMetadata)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(replaced[McpNativeBoundaryTestData.ContentField],
            expected[McpNativeBoundaryTestData.ContentField])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(replaced[McpNativeBoundaryTestData.StructuredContentField],
            expected[McpNativeBoundaryTestData.StructuredContentField])).IsTrue();
        await Assert.That(replaced[McpNativeBoundaryTestData.IsErrorField]?.GetValue<bool>()).IsEqualTo(failure);
        await Assert.That(JsonSerializer.Serialize<JsonRpcMessage>(response, McpJsonUtilities.DefaultOptions))
            .DoesNotContain(McpNativeBoundaryTestData.OldPayload);
    }

    /// <summary>Missing metadata or a null discarded result produces only the actual native serialization of the fresh tool result.</summary>
    /// <param name="nullResult">Whether the discarded response has no result node.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReplacementWithoutMetadataMatchesNativeToolResult(bool nullResult)
    {
        var oldResult = nullResult ? null : new JsonObject
        { [McpNativeBoundaryTestData.OldPayloadField] = McpNativeBoundaryTestData.OldPayload };
        var response = new JsonRpcResponse { Id = new RequestId(McpOutputTestData.NativeIdentifier), Result = oldResult };
        using var owner = McpNativeBoundaryTestData.Reply(failure: true);
        var tool = owner.ToolResult();
        var expected = JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)
            ?? throw new InvalidOperationException(McpNativeBoundaryTestData.MissingNode);
        McpResponseReplacement.Apply(response, tool);
        await Assert.That(JsonNode.DeepEquals(response.Result, expected)).IsTrue();
        await Assert.That(response.Result?[McpNativeBoundaryTestData.MetaField]).IsNull();
        await Assert.That(response.Context).IsNull();
        await Assert.That(JsonSerializer.Serialize<JsonRpcMessage>(response, McpJsonUtilities.DefaultOptions))
            .DoesNotContain(McpNativeBoundaryTestData.OldPayload);
    }
}
