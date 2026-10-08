using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteDocumentWire
{
    private const int FirstSignatureIndex = 0;
    private const int MinimumBodyBytes = 1;
    private const int NoContentEncodings = 0;

    internal static byte[] Encode<T>(T value, long maximumBytes = RemoteDocumentProtocol.MaximumBodyBytes)
    {
        var bytes = NativeSerialization.Measure(value);
        if (maximumBytes is < MinimumBodyBytes or > RemoteDocumentProtocol.MaximumBodyBytes
            || bytes < MinimumBodyBytes || bytes > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteDocumentProtocol.InvalidProof); }
        return NativeSerialization.Serialize(value);
    }

    internal static async Task<byte[]> ReadRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethods.Post || request.Path != RemoteDocumentProtocol.Path
            || request.QueryString.HasValue || request.ContentType != RemoteDocumentProtocol.ContentType
            || request.ContentLength is not (>= MinimumBodyBytes and <= RemoteDocumentProtocol.MaximumBodyBytes)
            || request.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.TransferEncoding)
            || request.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.ContentEncoding))
        { throw Errors.Fail(ErrorCode.Validation, RemoteDocumentProtocol.InvalidProof); }
        var bytes = new byte[checked((int)request.ContentLength.Value)];
        await request.Body.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    internal static async Task<byte[]> ReadReplyAsync(HttpContent content, CancellationToken cancellationToken,
        long maximumBytes = RemoteDocumentProtocol.MaximumBodyBytes)
    {
        if (maximumBytes is < MinimumBodyBytes or > RemoteDocumentProtocol.MaximumBodyBytes
            || content.Headers.ContentType?.ToString() != RemoteDocumentProtocol.ContentType
            || content.Headers.ContentLength is not (>= MinimumBodyBytes and <= RemoteDocumentProtocol.MaximumBodyBytes)
            || content.Headers.ContentLength > maximumBytes
            || content.Headers.ContentEncoding.Count != NoContentEncodings)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.InvalidProof); }
        var bytes = new byte[checked((int)content.Headers.ContentLength.Value)];
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    internal static long ReplyMaximum(RemoteDocumentCallV1 call)
        => call.QueryLeaf is { } leaf
            ? Math.Min(RemoteDocumentProtocol.MaximumBodyBytes, PartitionQueryParallelRetention.ReplyBytes(leaf.Plan))
            : RemoteDocumentProtocol.MaximumBodyBytes;

    internal static RemoteDocumentCallV1 DecodeCall(ReadOnlySpan<byte> body)
    {
        try
        { return NativeSerialization.Deserialize<RemoteDocumentCallV1>(body); }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        { throw Errors.Fail(ErrorCode.Validation, RemoteDocumentProtocol.InvalidProof); }
    }

    internal static string Signature(IHeaderDictionary headers)
    {
        const int SingleSignature = 1;
        if (!headers.TryGetValue(RemoteDocumentProtocol.SignatureHeader, out var signatures)
            || signatures.Count != SingleSignature || signatures[FirstSignatureIndex] is not { } signature)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        return signature;
    }
}
