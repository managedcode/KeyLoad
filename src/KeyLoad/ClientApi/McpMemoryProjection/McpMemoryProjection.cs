using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Projects conservative retained-memory peaks for MCP ingress, decoding, operation and execution reply stages.</summary>
internal sealed class McpMemoryProjection
{
    private readonly McpExecutionOptions settings;
    private const int MinimumPropertyWireBytes = 4;
    private const long InputExpansionMultiplier = 6;

    internal McpMemoryProjection(IOptions<McpExecutionOptions> options)
    {
        settings = options.Value;
        settings.Validate();
    }

    /// <summary>Charges body capacity, parser metadata, worst bounded shape, authentication bytes and safe failure space.</summary>
    /// <param name="capacity">The complete private request-body capacity.</param>
    /// <returns>A conservative retained-byte charge before framing and authentication.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is negative or exceeds the configured Kestrel request ceiling.</exception>
    internal long Ingress(int capacity)
    {
        ValidateBound(capacity, settings.MaximumRequestCapacityBytes, nameof(capacity));
        var tokens = Math.Min((long)capacity + 1, settings.MaximumTokens);
        var properties = Math.Min(capacity / MinimumPropertyWireBytes, settings.MaximumProperties);
        return checked(capacity + settings.IngressScratchBytes +
            McpMemoryProjectionComponents.Metadata(capacity, tokens) +
            McpMemoryProjectionComponents.Structure(capacity, tokens, properties) +
            McpMemoryProjectionComponents.Escaping(capacity) +
            settings.MaximumAuthenticationBytes + settings.SafeFailureBytes);
    }

    /// <summary>Precharges the strict framing inspector for the worst shape that fits actual bounded bytes.</summary>
    /// <param name="bytes">The bounded UTF-8 buffer capacity to inspect.</param>
    /// <returns>The complete named inspection components before the scanner allocates metadata or decoded strings.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The byte capacity is negative or exceeds the Kestrel frame ceiling.</exception>
    internal long Inspection(int bytes)
    {
        ValidateBound(bytes, settings.MaximumRequestCapacityBytes, nameof(bytes));
        var tokens = Math.Min((long)bytes + 1, settings.MaximumTokens);
        var properties = Math.Min(bytes / MinimumPropertyWireBytes, settings.MaximumProperties);
        return checked(McpMemoryProjectionComponents.Metadata(bytes, tokens) +
            McpMemoryProjectionComponents.Structure(bytes, tokens, properties) +
            McpMemoryProjectionComponents.Escaping(bytes));
    }

    /// <summary>Adds actual bounded authentication bytes and worst-shape inspection before inspecting the reply.</summary>
    /// <param name="capacity">The complete private HTTP body capacity already retained by ingress.</param>
    /// <param name="authBytes">The actual canonical authentication reply length.</param>
    /// <returns>The ingress charge, actual reply owner and framing-inspection peak.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either capacity is negative or exceeds its accepted ceiling.</exception>
    internal long AuthenticationScan(int capacity, int authBytes)
    {
        ValidateBound(capacity, settings.MaximumRequestCapacityBytes, nameof(capacity));
        ValidateBound(authBytes, settings.MaximumAuthenticationBytes, nameof(authBytes));
        return checked(Ingress(capacity) + authBytes + Inspection(authBytes));
    }

    /// <summary>Adds actual authentication decoding and principal-retention headroom to the ingress charge.</summary>
    /// <param name="capacity">The complete private request-body capacity already charged by ingress.</param>
    /// <param name="authBytes">The actual bounded authentication reply byte count.</param>
    /// <param name="authShape">The inspected authentication reply token and property counts.</param>
    /// <returns>The ingress charge plus authentication document and decoded-string ownership.</returns>
    internal long Authentication(int capacity, int authBytes, McpFrameShape authShape)
    {
        ValidateBound(capacity, settings.MaximumRequestCapacityBytes, nameof(capacity));
        ValidateBound(authBytes, settings.MaximumAuthenticationBytes, nameof(authBytes));
        ValidateShape(authShape, nameof(authShape));
        return checked(Ingress(capacity) + authBytes +
            McpMemoryProjectionComponents.Metadata(authBytes, authShape.TokenCount) +
            McpMemoryProjectionComponents.Structure(authBytes, authShape.TokenCount, authShape.PropertyCount) +
            McpMemoryProjectionComponents.Escaping(authBytes));
    }

    /// <summary>Grows the current reservation for framing a canonical result before inspecting its bytes.</summary>
    /// <param name="held">The complete existing reservation that must remain held.</param>
    /// <param name="replyBytes">The actual bounded canonical result byte length.</param>
    /// <returns>The held charge plus all worst-shape inspection components for the result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The held charge is negative or reply exceeds its canonical ceiling.</exception>
    internal long ReplyScan(long held, int replyBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(held);
        ValidateBound(replyBytes, settings.MaximumDataReplyBytes, nameof(replyBytes));
        return checked(held + Inspection(replyBytes));
    }

    /// <summary>Projects all overlapping retained input, principal, canonical-payload and pre-operation reply buffers.</summary>
    /// <param name="input">The immutable dimensions of the already inspected request and actual principal reply.</param>
    /// <param name="maximumReplyBytes">The canonical operation reply ceiling.</param>
    /// <param name="protocolReply">Whether execution protocol/SSE reply capacity must be charged before dispatch.</param>
    /// <returns>The complete conservative byte charge held before invoking the execution operation handler.</returns>
    internal long BeforeOperation(McpInputMemory input, int maximumReplyBytes, bool protocolReply)
    {
        const long MaximumPayloadBytesScaleFactor = 2L;
        const int HeldEmptyCount = 0;
        const int BeforeOperationAbsentCount = 0;

        ValidateInput(input, maximumReplyBytes);
        var ingressAndPrincipal = Authentication(input.Capacity, input.AuthenticationBytes, input.AuthenticationShape);
        var untypedMetadata = McpMemoryProjectionComponents.Metadata(input.WireBytes, input.Shape.TokenCount);
        var untypedStructure = McpMemoryProjectionComponents.Structure(input.WireBytes,
            input.Shape.TokenCount, input.Shape.PropertyCount);
        var typedParameterMetadata = McpMemoryProjectionComponents.Metadata(input.WireBytes, input.Shape.TokenCount);
        var typedParameterStructure = McpMemoryProjectionComponents.Structure(input.WireBytes,
            input.Shape.TokenCount, input.Shape.PropertyCount);
        var expansionHint = InputExpansion(input.WireBytes);
        var payloadWriter = McpMemoryProjectionComponents.Writer(input.WireBytes, expansionHint);
        var canonicalWriter = McpMemoryProjectionComponents.Writer(input.MaximumPayloadBytes, expansionHint);
        var canonicalEscaping = McpMemoryProjectionComponents.Escaping(input.MaximumPayloadBytes);
        var canonicalDtoStructure = McpMemoryProjectionComponents.Structure(input.MaximumPayloadBytes,
            input.Shape.TokenCount, input.Shape.PropertyCount);
        var canonicalBufferAndReturnedCopy = checked(MaximumPayloadBytesScaleFactor * input.MaximumPayloadBytes);
        var operationReply = maximumReplyBytes;
        var validationCapacity = checked((long)maximumReplyBytes + settings.EnvelopeAllowanceBytes);
        var controlReplyPeak = protocolReply ? AfterReply(HeldEmptyCount, settings.MaximumControlReplyBytes,
            WorstShape(settings.MaximumControlReplyBytes)) : BeforeOperationAbsentCount;

        return checked(ingressAndPrincipal + untypedMetadata + untypedStructure +
            typedParameterMetadata + typedParameterStructure + payloadWriter + canonicalWriter +
            canonicalEscaping + canonicalDtoStructure + canonicalBufferAndReturnedCopy +
            operationReply + validationCapacity + controlReplyPeak);
    }

    /// <summary>Grows retained ownership for the actual wrapped result and execution SDK SSE serialization overlap.</summary>
    /// <param name="held">The existing reservation that must remain held throughout reply construction.</param>
    /// <param name="replyBytes">The actual bounded canonical reply length.</param>
    /// <param name="replyShape">The inspected reply token and property counts.</param>
    /// <returns>The original reservation plus wrapper, metadata, DOM, writer and escaping peaks.</returns>
    internal long AfterReply(long held, int replyBytes, McpFrameShape replyShape)
    {
        const int WriterScaleFactor = 2;
        const int WrapperCapacityScaleFactor = 2;

        ArgumentOutOfRangeException.ThrowIfNegative(held);
        ValidateBound(replyBytes, settings.MaximumDataReplyBytes, nameof(replyBytes));
        ValidateShape(replyShape, nameof(replyShape));

        var wrapperCapacity = checked((long)replyBytes + settings.EnvelopeAllowanceBytes);
        var encodedUpper = checked(InputExpansionMultiplier * wrapperCapacity + settings.EnvelopeAllowanceBytes);
        var wrapperTokens = checked((long)replyShape.TokenCount + settings.EnvelopeItems);
        var wrapperProperties = checked((long)replyShape.PropertyCount + settings.EnvelopeItems);
        var runtimeMetadata = McpMemoryProjectionComponents.Metadata(encodedUpper, wrapperTokens);
        var runtimeStructure = McpMemoryProjectionComponents.Structure(encodedUpper, wrapperTokens, wrapperProperties);
        var runtimeWriter = McpMemoryProjectionComponents.Writer(wrapperCapacity, encodedUpper);
        var escaping = McpMemoryProjectionComponents.Escaping(encodedUpper);
        var sseWriters = checked(WriterScaleFactor * McpMemoryProjectionComponents.Writer(encodedUpper, encodedUpper));

        var simultaneousWrapperOwners = checked(WrapperCapacityScaleFactor * wrapperCapacity + encodedUpper);
        return checked(held + simultaneousWrapperOwners + runtimeMetadata + runtimeStructure + runtimeWriter + escaping + sseWriters);
    }

    internal long ProgressNotification(int bytes)
    {
        const long NoRetainedProgressBytes = 0;
        return AfterReply(NoRetainedProgressBytes, bytes, WorstShape(bytes));
    }

    private void ValidateInput(McpInputMemory input, int maximumReplyBytes)
    {
        ValidateBound(input.Capacity, settings.MaximumRequestCapacityBytes, nameof(input.Capacity));
        ValidateBound(input.WireBytes, input.Capacity, nameof(input.WireBytes));
        ValidateBound(input.MaximumPayloadBytes, settings.MaximumCanonicalPayloadBytes, nameof(input.MaximumPayloadBytes));
        ValidateBound(input.AuthenticationBytes, settings.MaximumAuthenticationBytes, nameof(input.AuthenticationBytes));
        ValidateBound(maximumReplyBytes, settings.MaximumDataReplyBytes, nameof(maximumReplyBytes));
        ValidateShape(input.Shape, nameof(input.Shape));
        ValidateShape(input.AuthenticationShape, nameof(input.AuthenticationShape));
    }

    private McpFrameShape WorstShape(int bytes) => new(
        (int)Math.Min((long)bytes + 1, settings.MaximumTokens),
        Math.Min(bytes / MinimumPropertyWireBytes, settings.MaximumProperties), settings.MaximumReplyDepth);

    private long InputExpansion(int wireBytes) => checked(InputExpansionMultiplier * wireBytes + settings.EnvelopeAllowanceBytes);

    private static void ValidateBound(int value, int maximum, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum, parameterName);
    }

    private void ValidateShape(McpFrameShape shape, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(shape.TokenCount, parameterName);
        ArgumentOutOfRangeException.ThrowIfNegative(shape.PropertyCount, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(shape.TokenCount, settings.MaximumTokens, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(shape.PropertyCount, settings.MaximumProperties, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(shape.PropertyCount, shape.TokenCount, parameterName);
        ArgumentOutOfRangeException.ThrowIfNegative(shape.Depth, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(shape.Depth, settings.MaximumDepth, parameterName);
    }
}
