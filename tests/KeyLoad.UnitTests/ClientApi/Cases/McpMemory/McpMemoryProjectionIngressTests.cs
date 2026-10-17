using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-004/005: conservative ingress and authentication ownership projection.</summary>
internal sealed class McpMemoryProjectionIngressTests
{
    private const int ZeroCapacity = 0;
    private const int OneByteCapacity = 1;
    private const long IngressAtZero = 17_076_956;
    private const long IngressAtOne = 17_077_675;
    private const long AuthenticationAtEmptyShape = 17_112_764;
    private const long EmptyMetadataBytes = 4_128;
    private const long EmptyWriterBytes = 512;
    private const long EmptyEscapingBytes = 528;
    private const long EmptyStructureBytes = 16_384;
    private const int SmallFrameBytes = 64;
    private const int SmallAuthenticationBytes = 16;

    /// <summary>Zero-byte projections retain fixed guards while capacity growth changes bounded framing headroom.</summary>
    [Test]
    public async Task IngressUsesWorstBoundedShapeAndPreservesExactCapacityAccounting()
    {
        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(ZeroCapacity)).IsEqualTo(IngressAtZero);
        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(OneByteCapacity)).IsEqualTo(IngressAtOne);
    }

    /// <summary>Named metadata, writer, escaping and structure equations keep their minimum rounded capacities.</summary>
    [Test]
    public async Task NamedComponentsMatchTheFrozenEmptyShapeEquations()
    {
        await Assert.That(McpMemoryProjectionComponents.Metadata(0, 0)).IsEqualTo(EmptyMetadataBytes);
        await Assert.That(McpMemoryProjectionComponents.Writer(0, 0)).IsEqualTo(EmptyWriterBytes);
        await Assert.That(McpMemoryProjectionComponents.Escaping(0)).IsEqualTo(EmptyEscapingBytes);
        await Assert.That(McpMemoryProjectionComponents.Structure(0, 0, 0)).IsEqualTo(EmptyStructureBytes);
    }

    /// <summary>Ingress capacity uses the configured 32 MiB Kestrel limit, independently of reply-size limits.</summary>
    [Test]
    public async Task IngressCapacityCeilingIsThirtyTwoMiB()
    {
        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(32 * 1024 * 1024)).IsGreaterThan(0L);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(32 * 1024 * 1024 + 1));
    }

    /// <summary>Actual authentication bytes and JSON shape add retained decode headroom to the ingress charge.</summary>
    [Test]
    public async Task AuthenticationProjectionGrowsWithActualBytesAndStructure()
    {
        var baseline = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(SmallFrameBytes);
        var emptyAuthentication = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0));
        var actualAuthentication = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, SmallAuthenticationBytes,
            new McpFrameShape(4, 1));
        var moreAuthentication = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, SmallAuthenticationBytes * 2,
            new McpFrameShape(8, 2));

        await Assert.That(emptyAuthentication).IsEqualTo(AuthenticationAtEmptyShape);
        await Assert.That(emptyAuthentication > baseline).IsTrue();
        await Assert.That(actualAuthentication > emptyAuthentication).IsTrue();
        await Assert.That(moreAuthentication > actualAuthentication).IsTrue();
    }

    /// <summary>Negative capacities and authentication sizes are rejected before projection arithmetic.</summary>
    [Test]
    public void IngressAndAuthenticationRejectNegativeInputs()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, -1, new McpFrameShape(0, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(-1, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0, -1)));
    }

    /// <summary>Authentication framing honors the frozen native limits instead of projecting impossible shapes.</summary>
    [Test]
    public async Task AuthenticationRejectsShapesBeyondNativeFramingCeilings()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(131_073, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 32_769)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(1, 2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0, 65)));
        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0, 64)))
            .IsEqualTo(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0)));
    }

    /// <summary>Authentication includes the inclusive sixteen-megabyte reply ceiling and rejects its first excess byte.</summary>
    [Test]
    public async Task AuthenticationReplyCeilingIsInclusive()
    {
        var accepted = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 16_777_216, new McpFrameShape(0, 0));
        await Assert.That(accepted > new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(SmallFrameBytes)).IsTrue();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(SmallFrameBytes, 16_777_217, new McpFrameShape(0, 0)));
    }
}
