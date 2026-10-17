using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteRequestRead
{
    internal static EventVectorMacCheckedCall Read(ReadOnlyMemory<byte> originalPayload,
        string originalSignature, RemoteDocumentMac actualCallerMac, EventVectorRemoteEncoding encoding,
        CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualCallerMac);
        ArgumentNullException.ThrowIfNull(encoding);
        originalCancellationToken.ThrowIfCancellationRequested();
        if (originalPayload.IsEmpty || originalPayload.Length > encoding.MaximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, EventVectorRemoteProtocol.Invalid);
        }
        if (originalSignature is not { Length: RemoteDocumentProtocol.SignatureCharacters })
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
        }
        var originalBytes = originalPayload.ToArray();
        EventVectorRemoteCall? call = null;
        var transferred = false;
        try
        {
            if (!actualCallerMac.Verify(originalBytes, originalSignature, reply: false))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
            }
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
                {
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(call.OriginalNativeRequest).Span);
                }
            }
        }
    }
}
