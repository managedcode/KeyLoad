using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.ClientApi;

/// <summary>AC-MCP-003/005/007: a fresh execution result retains injected metadata and exact message identity.</summary>
internal sealed class McpResponseReplacementTests
{
    /// <summary>Replacement moves the original metadata node without copying old business payload or changing execution identity.</summary>
    /// <param name="failure">Whether to replace with the genuine safe failure rather than success tool result.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReplacementPreservesMetadataAndIdentity(bool failure)
    {
        var metadata = McpBoundaryTestData.Metadata();
        var originalMetadata = JsonSerializer.SerializeToUtf8Bytes(metadata, McpJsonUtilities.DefaultOptions);
        var oldResult = new JsonObject
        {
            [McpBoundaryTestData.OldPayloadField] = McpBoundaryTestData.OldPayload,
            [McpBoundaryTestData.MetaField] = metadata
        };
        var context = new JsonRpcMessageContext();
        var identifier = new RequestId(McpOutputTestData.RuntimeIdentifier);
        var response = new JsonRpcResponse { Id = identifier, Context = context, Result = oldResult };
        using var owner = McpBoundaryTestData.Reply(failure);
        var tool = owner.ToolResult();
        var expected = JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)
            ?? throw new InvalidOperationException(McpBoundaryTestData.MissingNode);
        McpResponseReplacement.Apply(response, tool);
        var replaced = response.Result as JsonObject
            ?? throw new InvalidOperationException(McpBoundaryTestData.MissingNode);
        await Assert.That(replaced).IsNotSameReferenceAs(oldResult);
        await Assert.That(replaced[McpBoundaryTestData.MetaField]).IsSameReferenceAs(metadata);
        await Assert.That(metadata.Parent).IsSameReferenceAs(replaced);
        await Assert.That(oldResult[McpBoundaryTestData.MetaField]).IsNull();
        await Assert.That(response.Context).IsSameReferenceAs(context);
        await Assert.That(response.Id).IsEqualTo(identifier);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(metadata, McpJsonUtilities.DefaultOptions)
            .AsSpan().SequenceEqual(originalMetadata)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(replaced[McpBoundaryTestData.ContentField],
            expected[McpBoundaryTestData.ContentField])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(replaced[McpBoundaryTestData.StructuredContentField],
            expected[McpBoundaryTestData.StructuredContentField])).IsTrue();
        await Assert.That(replaced[McpBoundaryTestData.IsErrorField]?.GetValue<bool>()).IsEqualTo(failure);
        await Assert.That(JsonSerializer.Serialize<JsonRpcMessage>(response, McpJsonUtilities.DefaultOptions))
            .DoesNotContain(McpBoundaryTestData.OldPayload);
    }

    /// <summary>Missing metadata or a null discarded result produces only the actual execution serialization of the fresh tool result.</summary>
    /// <param name="nullResult">Whether the discarded response has no result node.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReplacementWithoutMetadataMatchesToolResult(bool nullResult)
    {
        var oldResult = nullResult ? null : new JsonObject
        {
            [McpBoundaryTestData.OldPayloadField] = McpBoundaryTestData.OldPayload
        };
        var response = new JsonRpcResponse { Id = new RequestId(McpOutputTestData.RuntimeIdentifier), Result = oldResult };
        using var owner = McpBoundaryTestData.Reply(failure: true);
        var tool = owner.ToolResult();
        var expected = JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)
            ?? throw new InvalidOperationException(McpBoundaryTestData.MissingNode);
        McpResponseReplacement.Apply(response, tool);
        await Assert.That(JsonNode.DeepEquals(response.Result, expected)).IsTrue();
        await Assert.That(response.Result?[McpBoundaryTestData.MetaField]).IsNull();
        await Assert.That(response.Context).IsNull();
        await Assert.That(JsonSerializer.Serialize<JsonRpcMessage>(response, McpJsonUtilities.DefaultOptions))
            .DoesNotContain(McpBoundaryTestData.OldPayload);
    }
}
