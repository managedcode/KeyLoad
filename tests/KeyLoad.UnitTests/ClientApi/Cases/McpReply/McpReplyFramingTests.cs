using System.Text;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/005/007: canonical replies leave room for the three native envelope containers.</summary>
internal sealed class McpReplyFramingTests
{
    private const int MaximumWireBytes = 65_536;
    private const int MaximumReplyDepth = 61;
    private const int MaximumInboundDepth = 64;
    private const string NullJson = "null";
    private const string BooleanJson = "true";
    private const string StringJson = "\"é\"";
    private const string ArrayJson = "[null,[],{\"value\":true}]";
    private const string ObjectJson = "{\"result\":{\"value\":null}}";
    private const string DuplicateJson = "{\"value\":null,\"\\u0076alue\":true}";

    /// <summary>Arbitrary scalar and container replies report actual tokens, properties and maximum depth.</summary>
    /// <param name="json">The genuine complete JSON value.</param>
    /// <param name="tokens">The expected reader-token count.</param>
    /// <param name="properties">The expected property-name count.</param>
    /// <param name="depth">The maximum container depth, zero for a scalar.</param>
    [Test]
    [Arguments(NullJson, 1, 0, 0)]
    [Arguments(BooleanJson, 1, 0, 0)]
    [Arguments(StringJson, 1, 0, 0)]
    [Arguments(ArrayJson, 9, 1, 2)]
    [Arguments(ObjectJson, 7, 2, 2)]
    public async Task ReplyReportsActualValueShape(string json, int tokens, int properties, int depth)
    {
        var wire = Encoding.UTF8.GetBytes(json);
        var shape = McpFrameBounds.InspectReply(wire, MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(shape.TokenCount).IsEqualTo(tokens);
        await Assert.That(shape.PropertyCount).IsEqualTo(properties);
        await Assert.That(shape.Depth).IsEqualTo(depth);
    }

    /// <summary>The exact reply depth succeeds and the next depth exhausts capacity before native wrapping.</summary>
    [Test]
    public async Task ReplyDepthAccepts61AndRejects62()
    {
        var exact = McpFrameBounds.InspectReply(McpResponseBoundaryTestData.NestedArrays(MaximumReplyDepth), MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(exact.Depth).IsEqualTo(MaximumReplyDepth);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectReply(McpResponseBoundaryTestData.NestedArrays(MaximumReplyDepth + 1), MaximumWireBytes, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>The reply ceiling does not narrow inbound object framing or ordinary value inspection.</summary>
    [Test]
    public async Task InboundAndValueInspectionRetainDepth64()
    {
        var inbound = McpFrameBounds.Inspect(McpResponseBoundaryTestData.NestedObject(MaximumInboundDepth), MaximumWireBytes, UnitMcpOptions.Execution());
        var value = McpFrameBounds.InspectValue(McpResponseBoundaryTestData.NestedArrays(MaximumInboundDepth), MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(inbound.Depth).IsEqualTo(MaximumInboundDepth);
        await Assert.That(value.Depth).IsEqualTo(MaximumInboundDepth);
    }

    /// <summary>Reply inspection keeps the ordinary encoded byte ceiling and duplicate-name validation.</summary>
    [Test]
    public async Task ReplyPreservesWireAndDuplicateBounds()
    {
        var wire = Encoding.UTF8.GetBytes(StringJson);
        var accepted = McpFrameBounds.InspectReply(wire, wire.Length, UnitMcpOptions.Execution());
        await Assert.That(accepted.TokenCount).IsEqualTo(1);
        var over = Assert.ThrowsExactly<KeyLoadException>(() => McpFrameBounds.InspectReply(wire, wire.Length - 1, UnitMcpOptions.Execution()));
        var duplicate = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectReply(Encoding.UTF8.GetBytes(DuplicateJson), MaximumWireBytes, UnitMcpOptions.Execution()));
        await Assert.That(over.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(duplicate.Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>Count-only construction retains the frozen optional depth default.</summary>
    [Test]
    public async Task CountOnlyShapeDefaultsDepthToZero()
    {
        var shape = new McpFrameShape(1, 0);
        await Assert.That(shape.Depth).IsEqualTo(0);
    }
}
