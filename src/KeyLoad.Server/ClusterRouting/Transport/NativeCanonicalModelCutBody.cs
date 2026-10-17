using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.AspNetCore.Http.Features;

namespace KeyLoad.Server;

internal static class NativeCanonicalModelCutBody
{
    private const int NoQueryItems = 0;
    private const int NoEncodingItems = 0;
    private const int EmptyBodyBytes = 0;
    private const int OverflowProbeBytes = 1;
    private const int BufferStart = 0;
    internal static async Task<NativeCanonicalModelCutRequest> ReadAsync(HttpRequest request, CancellationToken token)
    {
        if (request.Query.Count != NoQueryItems || request.Headers.ContentEncoding.Count != NoEncodingItems
            || !string.Equals(request.ContentType, NativeCanonicalModelCutDiagnosticProtocol.ContentType, StringComparison.OrdinalIgnoreCase)
            || request.ContentLength is <= EmptyBodyBytes
            || request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == false)
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        if (request.ContentLength > NativeCanonicalModelCutCodec.MaximumRecordBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
        var buffer = new byte[NativeCanonicalModelCutCodec.MaximumRecordBytes + OverflowProbeBytes];
        var length = EmptyBodyBytes;
        while (length < buffer.Length)
        {
            token.ThrowIfCancellationRequested();
            var read = await request.Body.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (read == EmptyBodyBytes)
            {
                break;
            }
            length += read;
        }
        token.ThrowIfCancellationRequested();
        if (length == buffer.Length)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
        if (length == EmptyBodyBytes)
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        NativeCanonicalModelCutRequest decoded;
        try
        {
            decoded = NativeSerialization.Deserialize<NativeCanonicalModelCutRequest>(buffer.AsSpan(BufferStart, length).ToArray())
                ?? throw NativeCanonicalModelCutCodec.Invalid();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)
            && error is not OperationCanceledException && error is not KeyLoadException)
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        NativeCanonicalModelCutCodec.ValidateRequest(decoded);
        token.ThrowIfCancellationRequested();
        return decoded;
    }
}
