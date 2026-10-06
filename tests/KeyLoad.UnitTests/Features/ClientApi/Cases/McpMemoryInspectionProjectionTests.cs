using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-004/005: framing inspection allocations are reserved before scanning actual bytes.</summary>
internal sealed class McpMemoryInspectionProjectionTests
{
    private const int SmallWireBytes = 32;
    private const int MaximumAuthenticationBytes = 16 * 1024 * 1024;
    private const int MaximumReplyBytes = 16 * 1024 * 1024;
    private const int MaximumFrameBytes = 32 * 1024 * 1024;
    private const long HeldBytes = 4096;

    /// <summary>Inspection precharge includes worst bounded token, property, string and metadata components.</summary>
    [Test]
    public async Task InspectionMatchesWorstShapeComponentsForActualByteCapacity()
    {
        var expected = InspectionComponents(SmallWireBytes);
        var actual = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Inspection(SmallWireBytes);
        var zeroShapeExpected = InspectionComponents(0);

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Inspection(0)).IsEqualTo(zeroShapeExpected);
        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Inspection(SmallWireBytes * 2)).IsGreaterThan(actual);
    }

    /// <summary>Authentication inspection is covered before parsing and includes its already-retained canonical bytes.</summary>
    [Test]
    public async Task AuthenticationScanAddsActualReplyInspectionToIngressBeforeDecode()
    {
        var expected = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(SmallWireBytes) + SmallWireBytes + InspectionComponents(SmallWireBytes);
        var actual = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AuthenticationScan(SmallWireBytes, SmallWireBytes);
        var maximum = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AuthenticationScan(0, MaximumAuthenticationBytes);

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(maximum > new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(0)).IsTrue();
    }

    /// <summary>Authentication scan accepts its inclusive canonical ceiling and rejects the first excess byte.</summary>
    [Test]
    public void AuthenticationScanValidatesActualBytesAndRequestCapacity()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AuthenticationScan(-1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AuthenticationScan(0, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AuthenticationScan(0, MaximumAuthenticationBytes + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AuthenticationScan(MaximumFrameBytes + 1, 0));
    }

    /// <summary>Reply scanning grows the current lease by actual bounded-byte inspection costs without shrinking it.</summary>
    [Test]
    public async Task ReplyScanAddsInspectionForActualCanonicalResultBeforeWrapperCreation()
    {
        var expected = HeldBytes + InspectionComponents(SmallWireBytes);
        var actual = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).ReplyScan(HeldBytes, SmallWireBytes);

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).ReplyScan(HeldBytes, SmallWireBytes * 2)).IsGreaterThan(actual);
        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).ReplyScan(HeldBytes, 0)).IsGreaterThan(HeldBytes);
    }

    /// <summary>Reply scan rejects negative and over-ceiling values before computing a reservation.</summary>
    [Test]
    public void ReplyScanValidatesHeldBytesAndCanonicalReplyCeiling()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).ReplyScan(-1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).ReplyScan(HeldBytes, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).ReplyScan(HeldBytes, MaximumReplyBytes + 1));
    }

    /// <summary>Overflow in held ownership is reported instead of returning an undercount.</summary>
    [Test]
    public void ReplyScanRejectsOverflow()
    {
        Assert.ThrowsExactly<OverflowException>(() => new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).ReplyScan(long.MaxValue, 0));
    }

    private static long InspectionComponents(int bytes)
    {
        var tokens = Math.Min((long)bytes + 1, 131_072);
        var properties = Math.Min(bytes / 4, 32_768);
        var shape = new McpFrameShape((int)tokens, properties);
        return McpMemoryProjectionComponents.Metadata(bytes, shape.TokenCount) +
            McpMemoryProjectionComponents.Structure(bytes, shape.TokenCount, shape.PropertyCount) +
            McpMemoryProjectionComponents.Escaping(bytes);
    }
}
