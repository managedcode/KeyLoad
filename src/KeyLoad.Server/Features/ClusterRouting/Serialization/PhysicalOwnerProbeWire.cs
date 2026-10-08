namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerProbeWire
{
    private const int MinimumBodyBytes = 1;
    private const int NoContentEncodings = 0;

    internal static byte[] Encode<T>(T value)
    {
        var bytes = NativeSerialization.Measure(value);
        if (bytes is < MinimumBodyBytes or > PhysicalOwnerProbeProtocol.MaximumBodyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, PhysicalOwnerProbeProtocol.InvalidProof); }
        return NativeSerialization.Serialize(value);
    }

    internal static async Task<byte[]> ReadRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethods.Post || request.Path != PhysicalOwnerProbeProtocol.Path
            || request.QueryString.HasValue || request.ContentType != PhysicalOwnerProbeProtocol.ContentType
            || request.ContentLength is not (>= MinimumBodyBytes and <= PhysicalOwnerProbeProtocol.MaximumBodyBytes)
            || request.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.TransferEncoding)
            || request.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.ContentEncoding))
        { throw Errors.Fail(ErrorCode.Validation, PhysicalOwnerProbeProtocol.InvalidProof); }
        var bytes = new byte[checked((int)request.ContentLength.Value)];
        await request.Body.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    internal static async Task<byte[]> ReadReplyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentType?.ToString() != PhysicalOwnerProbeProtocol.ContentType
            || content.Headers.ContentLength is not (>= MinimumBodyBytes and <= PhysicalOwnerProbeProtocol.MaximumBodyBytes)
            || content.Headers.ContentEncoding.Count != NoContentEncodings)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.InvalidProof); }
        var bytes = new byte[checked((int)content.Headers.ContentLength.Value)];
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    internal static PhysicalOwnerProbeCallV1 DecodeCall(ReadOnlySpan<byte> body)
    {
        try
        { return NativeSerialization.Deserialize<PhysicalOwnerProbeCallV1>(body); }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        { throw Errors.Fail(ErrorCode.Validation, PhysicalOwnerProbeProtocol.InvalidProof); }
    }

    internal static string Signature(IHeaderDictionary headers)
    {
        const int SingleSignature = 1;
        const int FirstSignature = 0;
        if (!headers.TryGetValue(PhysicalOwnerProbeProtocol.SignatureHeader, out var signatures)
            || signatures.Count != SingleSignature || signatures[FirstSignature] is not { } signature)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PhysicalOwnerProbeProtocol.InvalidProof); }
        return signature;
    }
}
