using KeyLoad.Server;

namespace KeyLoad.UnitTests.ClientApi;

/// <summary>AC-MCP-004/007: post-reply projections are monotonic and fail closed on invalid arithmetic.</summary>
internal sealed class McpMemoryProjectionReplyTests
{
    private const int ReplyBytes = 128;
    private const long HeldBytes = 4096;
    private const int RuntimeEnvelopeBytes = 64 * 1024;
    private const int EnvelopeItems = 1_024;
    private static readonly McpFrameShape ReplyShape = new(12, 3);

    /// <summary>Reply bytes and execution wrapper shape increase the retained output peak without releasing held bytes.</summary>
    [Test]
    public async Task AfterReplyAddsMonotonicWrapperAndSseHeadroom()
    {
        var initial = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(HeldBytes, 0, new McpFrameShape(0, 0));
        var reply = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(HeldBytes, ReplyBytes, ReplyShape);
        var largerReply = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(HeldBytes, ReplyBytes * 2, ReplyShape);
        var largerShape = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(HeldBytes, ReplyBytes,
            new McpFrameShape(ReplyShape.TokenCount * 2, ReplyShape.PropertyCount * 2));
        var wrapperCapacity = ReplyBytes + RuntimeEnvelopeBytes;
        var encodedUpper = 6L * wrapperCapacity + RuntimeEnvelopeBytes;
        var wrapperTokens = ReplyShape.TokenCount + EnvelopeItems;
        var wrapperProperties = ReplyShape.PropertyCount + EnvelopeItems;
        var expectedReply = HeldBytes + 2L * wrapperCapacity + encodedUpper +
                            McpMemoryProjectionComponents.Metadata(encodedUpper, wrapperTokens) +
                            McpMemoryProjectionComponents.Structure(encodedUpper, wrapperTokens, wrapperProperties) +
                            McpMemoryProjectionComponents.Writer(wrapperCapacity, encodedUpper) +
                            McpMemoryProjectionComponents.Escaping(encodedUpper) +
                            2 * McpMemoryProjectionComponents.Writer(encodedUpper, encodedUpper);

        await Assert.That(initial >= HeldBytes).IsTrue();
        await Assert.That(reply > initial).IsTrue();
        await Assert.That(largerReply > reply).IsTrue();
        await Assert.That(largerShape > reply).IsTrue();
        await Assert.That(reply).IsEqualTo(expectedReply);
    }

    /// <summary>Reply projection rejects negative ownership, bytes, or JSON shape values.</summary>
    [Test]
    public void AfterReplyRejectsNegativeInputs()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(-1, ReplyBytes, ReplyShape));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(HeldBytes, -1, ReplyShape));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(HeldBytes, ReplyBytes, new McpFrameShape(1, -1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(HeldBytes, ReplyBytes, new McpFrameShape(1, 0, 65)));
    }

    /// <summary>Large additions throw instead of wrapping a held reservation into a smaller projection.</summary>
    [Test]
    public void AfterReplyRejectsOverflowRatherThanReturningAnUndercount()
    {
        Assert.ThrowsExactly<OverflowException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(long.MaxValue, ReplyBytes, ReplyShape));
    }
}
