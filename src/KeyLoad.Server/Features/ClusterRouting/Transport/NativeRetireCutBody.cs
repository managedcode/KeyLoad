namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeRetireCutBody
{
    private const int Empty = 0;
    private const int Overflow = 1;

    internal static async Task<NativeRetireCutRequest> ReadAsync(HttpRequest request, CancellationToken token)
    {
        if (request.Query.Count != Empty || request.Headers.ContentEncoding.Count != Empty
            || request.ContentType != NativeCanonicalModelCutDiagnosticProtocol.ContentType
            || request.ContentLength is <= Empty or > NativeCanonicalModelCutCodec.MaximumRecordBytes)
        {
            throw NativeRetireCutWire.Invalid();
        }
        var bytes = new byte[NativeCanonicalModelCutCodec.MaximumRecordBytes + Overflow];
        var count = Empty;
        while (count < bytes.Length)
        {
            var read = await request.Body.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
            if (read == Empty)
            {
                break;
            }
            count += read;
        }
        if (count is <= Empty or > NativeCanonicalModelCutCodec.MaximumRecordBytes)
        {
            throw NativeRetireCutWire.Invalid();
        }
        var result = NativeSerialization.Deserialize<NativeRetireCutRequest>(bytes.AsSpan(Empty, count).ToArray());
        NativeRetireCutWire.Validate(result);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
