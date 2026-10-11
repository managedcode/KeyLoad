using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteRequestRead
{
    internal static async Task<EventVectorMacCheckedCall> ReadAsync(HttpRequest request,
        RemoteDocumentMac actualCallerMac, EventVectorRemoteEncoding encoding,
        CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actualCallerMac);
        ArgumentNullException.ThrowIfNull(encoding);
        if (request.ContentLength is { } declaredBytes && declaredBytes > encoding.MaximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, EventVectorRemoteProtocol.Invalid); }
        var originalBytes = await RemoteDocumentWire.ReadRequestAsync(request,
            EventVectorRemoteProtocol.Path, originalCancellationToken).ConfigureAwait(false);
        EventVectorRemoteCall? call = null;
        var transferred = false;
        try
        {
            var originalSignature = RemoteDocumentWire.Signature(request.Headers);
            if (!actualCallerMac.Verify(originalBytes, originalSignature, reply: false))
            { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
            call = encoding.DecodeAuthenticatedCall(originalBytes);
            originalCancellationToken.ThrowIfCancellationRequested();
            var result = new EventVectorMacCheckedCall(call, originalBytes, originalSignature);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                CryptographicOperations.ZeroMemory(originalBytes);
                if (call is not null)
                { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(call.OriginalNativeRequest).Span); }
            }
        }
    }
}
