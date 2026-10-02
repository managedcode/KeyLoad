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
        await Assert.That(McpMemoryProjection.Ingress(ZeroCapacity)).IsEqualTo(IngressAtZero);
        await Assert.That(McpMemoryProjection.Ingress(OneByteCapacity)).IsEqualTo(IngressAtOne);
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
        await Assert.That(McpMemoryProjection.Ingress(32 * 1024 * 1024)).IsGreaterThan(0L);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => McpMemoryProjection.Ingress(32 * 1024 * 1024 + 1));
    }

    /// <summary>Actual authentication bytes and JSON shape add retained decode headroom to the ingress charge.</summary>
    [Test]
    public async Task AuthenticationProjectionGrowsWithActualBytesAndStructure()
    {
        var baseline = McpMemoryProjection.Ingress(SmallFrameBytes);
        var emptyAuthentication = McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0));
        var actualAuthentication = McpMemoryProjection.Authentication(SmallFrameBytes, SmallAuthenticationBytes,
            new McpFrameShape(4, 1));
        var moreAuthentication = McpMemoryProjection.Authentication(SmallFrameBytes, SmallAuthenticationBytes * 2,
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
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => McpMemoryProjection.Ingress(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.Authentication(SmallFrameBytes, -1, new McpFrameShape(0, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(-1, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0, -1)));
    }

    /// <summary>Authentication framing honors the frozen native limits instead of projecting impossible shapes.</summary>
    [Test]
    public async Task AuthenticationRejectsShapesBeyondNativeFramingCeilings()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(131_073, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 32_769)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(1, 2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0, 65)));
        await Assert.That(McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0, 64)))
            .IsEqualTo(McpMemoryProjection.Authentication(SmallFrameBytes, 0, new McpFrameShape(0, 0)));
    }

    /// <summary>Authentication includes the inclusive sixteen-megabyte reply ceiling and rejects its first excess byte.</summary>
    [Test]
    public async Task AuthenticationReplyCeilingIsInclusive()
    {
        var accepted = McpMemoryProjection.Authentication(SmallFrameBytes, 16_777_216, new McpFrameShape(0, 0));
        await Assert.That(accepted > McpMemoryProjection.Ingress(SmallFrameBytes)).IsTrue();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.Authentication(SmallFrameBytes, 16_777_217, new McpFrameShape(0, 0)));
    }
}

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
        var baseline = McpMemoryProjection.BeforeOperation(input, MaximumReplyBytes, protocolReply: false);
        var largerPayload = McpMemoryProjection.BeforeOperation(input with { MaximumPayloadBytes = MaximumPayloadBytes * 2 },
            MaximumReplyBytes, protocolReply: false);
        var largerFrame = McpMemoryProjection.BeforeOperation(input with
        {
            Shape = new McpFrameShape(FrameShape.TokenCount * 2, FrameShape.PropertyCount * 2)
        }, MaximumReplyBytes, protocolReply: false);
        var largerPrincipal = McpMemoryProjection.BeforeOperation(input with
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
        var expected = McpMemoryProjection.Authentication(input.Capacity, input.AuthenticationBytes, input.AuthenticationShape) +
            2 * McpMemoryProjectionComponents.Metadata(input.WireBytes, shape.TokenCount) +
            2 * McpMemoryProjectionComponents.Structure(input.WireBytes, shape.TokenCount, shape.PropertyCount) +
            McpMemoryProjectionComponents.Writer(input.WireBytes, expansionHint) +
            McpMemoryProjectionComponents.Writer(input.MaximumPayloadBytes, expansionHint) +
            McpMemoryProjectionComponents.Escaping(input.MaximumPayloadBytes) +
            McpMemoryProjectionComponents.Structure(input.MaximumPayloadBytes, shape.TokenCount, shape.PropertyCount) +
            2L * input.MaximumPayloadBytes + MaximumReplyBytes + nativeValidationCapacity;

        await Assert.That(McpMemoryProjection.BeforeOperation(input, MaximumReplyBytes, protocolReply: false))
            .IsEqualTo(expected);
    }

    /// <summary>Protocol-mode precharge includes the full bounded native control reply peak.</summary>
    [Test]
    public async Task ProtocolReplyPrechargesMoreThanTheCanonicalOperationReply()
    {
        var input = CreateInput();
        var operation = McpMemoryProjection.BeforeOperation(input, MaximumReplyBytes, protocolReply: false);
        var protocol = McpMemoryProjection.BeforeOperation(input, MaximumReplyBytes, protocolReply: true);
        var controlPeak = McpMemoryProjection.AfterReply(0, MaximumControlReplyBytes,
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
        var maximumRequestCharge = McpMemoryProjection.BeforeOperation(request, MaximumReplyBytes, protocolReply: false);

        await Assert.That(maximumRequestCharge > 0).IsTrue();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => McpMemoryProjection.Ingress(MaximumRequestCapacityBytes + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => McpMemoryProjection.BeforeOperation(
            request with { MaximumPayloadBytes = MaximumRequestCapacityBytes + 1 }, MaximumReplyBytes, false));
    }

    /// <summary>All pre-operation size inputs reject negative values before computing component sums.</summary>
    [Test]
    public void BeforeOperationRejectsNegativeBoundsAndShapeCounts()
    {
        var input = CreateInput();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.BeforeOperation(input with { Capacity = -1 }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.BeforeOperation(input with { WireBytes = -1 }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.BeforeOperation(input with { MaximumPayloadBytes = -1 }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.BeforeOperation(input with { WireBytes = FrameCapacity + 1 }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.BeforeOperation(input with { AuthenticationShape = new McpFrameShape(1, 2) }, MaximumReplyBytes, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.BeforeOperation(input, -1, false));
    }

    private static McpInputMemory CreateInput() => new(FrameCapacity, WireBytes, FrameShape,
        MaximumPayloadBytes, AuthenticationBytes, AuthenticationShape);
}

/// <summary>AC-MCP-004/007: post-reply projections are monotonic and fail closed on invalid arithmetic.</summary>
internal sealed class McpMemoryProjectionReplyTests
{
    private const int ReplyBytes = 128;
    private const long HeldBytes = 4096;
    private const int NativeEnvelopeBytes = 64 * 1024;
    private const int NativeEnvelopeItems = 1_024;
    private static readonly McpFrameShape ReplyShape = new(12, 3);

    /// <summary>Reply bytes and native wrapper shape increase the retained output peak without releasing held bytes.</summary>
    [Test]
    public async Task AfterReplyAddsMonotonicWrapperAndSseHeadroom()
    {
        var initial = McpMemoryProjection.AfterReply(HeldBytes, 0, new McpFrameShape(0, 0));
        var reply = McpMemoryProjection.AfterReply(HeldBytes, ReplyBytes, ReplyShape);
        var largerReply = McpMemoryProjection.AfterReply(HeldBytes, ReplyBytes * 2, ReplyShape);
        var largerShape = McpMemoryProjection.AfterReply(HeldBytes, ReplyBytes,
            new McpFrameShape(ReplyShape.TokenCount * 2, ReplyShape.PropertyCount * 2));
        var wrapperCapacity = ReplyBytes + NativeEnvelopeBytes;
        var encodedUpper = 6L * wrapperCapacity + NativeEnvelopeBytes;
        var wrapperTokens = ReplyShape.TokenCount + NativeEnvelopeItems;
        var wrapperProperties = ReplyShape.PropertyCount + NativeEnvelopeItems;
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
            McpMemoryProjection.AfterReply(-1, ReplyBytes, ReplyShape));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.AfterReply(HeldBytes, -1, ReplyShape));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.AfterReply(HeldBytes, ReplyBytes, new McpFrameShape(1, -1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            McpMemoryProjection.AfterReply(HeldBytes, ReplyBytes, new McpFrameShape(1, 0, 65)));
    }

    /// <summary>Large additions throw instead of wrapping a held reservation into a smaller projection.</summary>
    [Test]
    public void AfterReplyRejectsOverflowRatherThanReturningAnUndercount()
    {
        Assert.ThrowsExactly<OverflowException>(() =>
            McpMemoryProjection.AfterReply(long.MaxValue, ReplyBytes, ReplyShape));
    }
}
