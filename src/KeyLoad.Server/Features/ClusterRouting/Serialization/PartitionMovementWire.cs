namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementWire
{
    private const int MinimumBodyBytes = 1;
    private const int SingleSignature = 1;
    private const int FirstSignature = 0;

    internal static async Task<byte[]> ReadAsync(HttpRequest request, int maximumBytes, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethods.Post || request.Path != PartitionMovementProtocol.Path
            || request.QueryString.HasValue || request.ContentType != PartitionMovementProtocol.ContentType
            || request.ContentLength is not >= MinimumBodyBytes || request.ContentLength > maximumBytes
            || request.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.TransferEncoding)
            || request.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.ContentEncoding))
        { throw Errors.Fail(ErrorCode.Validation, PartitionMovementProtocol.InvalidProof); }
        var body = new byte[checked((int)request.ContentLength.Value)];
        await request.Body.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return body;
    }

    internal static string Signature(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(PartitionMovementProtocol.SignatureHeader, out var values)
            || values.Count != SingleSignature || values[FirstSignature] is not { } signature)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        return signature;
    }

    internal static byte[] Encode(PartitionMovementTransportReply reply, int maximumBytes)
    {
        var measured = NativeSerialization.Measure(reply);
        if (measured < MinimumBodyBytes || measured > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, PartitionMovementProtocol.Unavailable); }
        return NativeSerialization.Serialize(reply);
    }
}
