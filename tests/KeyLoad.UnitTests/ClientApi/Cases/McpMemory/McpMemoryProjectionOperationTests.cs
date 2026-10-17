using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-004/005: pre-operation reservations account for inputs, serializers and reply modes.</summary>
internal sealed class McpMemoryProjectionOperationTests
{
    private const int MaximumRequestCapacityBytes = 32 * 1024 * 1024;
    private const int MaximumControlReplyBytes = 64 * 1024;
    private const int InputExpansionAllowanceBytes = 64 * 1024;
    private const int MaximumControlTokens = 65_537;
    private const int MaximumControlProperties = 16_384;
    private const int MaximumControlResultDepth = 61;
    private const int FrameCapacity = 64;
    private const int WireBytes = 32;
    private const int MaximumPayloadBytes = 128;
    private const int MaximumReplyBytes = 256;
    private const int AuthenticationBytes = 16;
    private static readonly McpFrameShape FrameShape = new(8, 2);
    private static readonly McpFrameShape AuthenticationShape = new(4, 1);

    /// <summary>A larger payload, retained principal and parsed shape each increase the pre-operation charge.</summary>
    [Test]
    public async Task BeforeOperationTracksRetainedInputsAndCanonicalOutputCapacity()
    {
        var input = CreateInput();
        var baseline = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input, MaximumReplyBytes, protocolReply: false);
        var largerPayload = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input with
        {
            MaximumPayloadBytes = MaximumPayloadBytes * 2
        },
            MaximumReplyBytes, protocolReply: false);
        var largerFrame = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input with
        {
            Shape = new McpFrameShape(FrameShape.TokenCount * 2, FrameShape.PropertyCount * 2)
        }, MaximumReplyBytes, protocolReply: false);
        var largerPrincipal = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input with
        {
            AuthenticationBytes = AuthenticationBytes * 2,
            AuthenticationShape = new McpFrameShape(AuthenticationShape.TokenCount * 2, AuthenticationShape.PropertyCount * 2)
        }, MaximumReplyBytes, protocolReply: false);

        await Assert.That(largerPayload > baseline).IsTrue();
        await Assert.That(largerFrame > baseline).IsTrue();
        await Assert.That(largerPrincipal > baseline).IsTrue();
    }

    /// <summary>Pre-operation math includes the native validation stream separately from canonical reply ownership.</summary>
    [Test]
    public async Task BeforeOperationAddsCanonicalAndNativeReplyCapacitySeparately()
    {
        var input = CreateInput();
        var shape = input.Shape;
        var expansionHint = 6L * input.WireBytes + InputExpansionAllowanceBytes;
        var nativeValidationCapacity = MaximumReplyBytes + InputExpansionAllowanceBytes;
        var expected = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Authentication(input.Capacity, input.AuthenticationBytes, input.AuthenticationShape) +
                       2 * McpMemoryProjectionComponents.Metadata(input.WireBytes, shape.TokenCount) +
                       2 * McpMemoryProjectionComponents.Structure(input.WireBytes, shape.TokenCount, shape.PropertyCount) +
                       McpMemoryProjectionComponents.Writer(input.WireBytes, expansionHint) +
                       McpMemoryProjectionComponents.Writer(input.MaximumPayloadBytes, expansionHint) +
                       McpMemoryProjectionComponents.Escaping(input.MaximumPayloadBytes) +
                       McpMemoryProjectionComponents.Structure(input.MaximumPayloadBytes, shape.TokenCount, shape.PropertyCount) +
                       2L * input.MaximumPayloadBytes + MaximumReplyBytes + nativeValidationCapacity;

        await Assert.That(new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input, MaximumReplyBytes, protocolReply: false))
            .IsEqualTo(expected);
    }

    /// <summary>Protocol-mode precharge includes the full bounded native control reply peak.</summary>
    [Test]
    public async Task ProtocolReplyPrechargesMoreThanTheCanonicalOperationReply()
    {
        var input = CreateInput();
        var operation = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input, MaximumReplyBytes, protocolReply: false);
        var protocol = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input, MaximumReplyBytes, protocolReply: true);
        var controlPeak = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).AfterReply(0, MaximumControlReplyBytes,
            new McpFrameShape(MaximumControlTokens, MaximumControlProperties, MaximumControlResultDepth));

        await Assert.That(protocol > operation).IsTrue();
        await Assert.That(protocol - operation).IsEqualTo(controlPeak);
    }

    /// <summary>Kestrel request and canonical-payload ceilings remain independent of the sixteen-megabyte reply ceiling.</summary>
    [Test]
    public async Task RequestAndPayloadCapacityAllowTheConfiguredThirtyTwoMiBCeiling()
    {
        var request = CreateInput() with
        {
            Capacity = MaximumRequestCapacityBytes,
            WireBytes = MaximumRequestCapacityBytes,
            MaximumPayloadBytes = MaximumRequestCapacityBytes
        };
        var maximumRequestCharge = new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(request, MaximumReplyBytes, protocolReply: false);

        await Assert.That(maximumRequestCharge > 0).IsTrue();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).Ingress(MaximumRequestCapacityBytes + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(
            request with
            {
                MaximumPayloadBytes = MaximumRequestCapacityBytes + 1
            }, MaximumReplyBytes, false));
    }

    /// <summary>All pre-operation size inputs reject negative values before computing component sums.</summary>
    [Test]
    public void BeforeOperationRejectsNegativeBoundsAndShapeCounts()
    {
        var input = CreateInput();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input with
            {
                Capacity = -1
            }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input with
            {
                WireBytes = -1
            }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input with
            {
                MaximumPayloadBytes = -1
            }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input with
            {
                WireBytes = FrameCapacity + 1
            }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input with
            {
                AuthenticationShape = new McpFrameShape(1, 2)
            }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new KeyLoad.Server.McpMemoryProjection(UnitMcpOptions.Execution()).BeforeOperation(input, -1, false));
    }

    private static McpInputMemory CreateInput() => new(FrameCapacity, WireBytes, FrameShape,
        MaximumPayloadBytes, AuthenticationBytes, AuthenticationShape);
}
