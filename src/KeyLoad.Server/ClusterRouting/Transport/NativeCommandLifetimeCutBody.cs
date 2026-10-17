using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.AspNetCore.Http.Features;

namespace KeyLoad.Server;

internal static class NativeCommandLifetimeCutBody
{
    private const int EmptyItems = 0;
    private const int EmptyBytes = 0;
    private const int OverflowByte = 1;
    private const int BufferStart = 0;

    internal static async Task<NativeCommandLifetimeCutRequest> ReadAsync(HttpRequest request, CancellationToken token)
    {
        if (request.Query.Count != EmptyItems || request.Headers.ContentEncoding.Count != EmptyItems
            || !string.Equals(request.ContentType, NativeCanonicalModelCutDiagnosticProtocol.ContentType, StringComparison.OrdinalIgnoreCase)
            || request.ContentLength is <= EmptyBytes
            || request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == false)
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        if (request.ContentLength > NativeCommandLifetimeCutWire.MaximumRecordBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
        var bytes = new byte[NativeCommandLifetimeCutWire.MaximumRecordBytes + OverflowByte];
        var length = EmptyBytes;
        while (length < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var read = await request.Body.ReadAsync(bytes.AsMemory(length), token).ConfigureAwait(false);
            if (read == EmptyBytes)
            {
                break;
            }
            length += read;
        }
        token.ThrowIfCancellationRequested();
        if (length == bytes.Length)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
        if (length == EmptyBytes)
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        NativeCommandLifetimeCutRequest result;
        try
        {
            result = NativeSerialization.Deserialize<NativeCommandLifetimeCutRequest>(bytes.AsSpan(BufferStart, length))
                ?? throw NativeCanonicalModelCutCodec.Invalid();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)
            && error is not OperationCanceledException && error is not KeyLoadException)
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        NativeCommandLifetimeCutCodec.Validate(result);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
