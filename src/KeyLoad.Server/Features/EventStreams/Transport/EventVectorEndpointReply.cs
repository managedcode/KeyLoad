using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorEndpointReply
{
    internal static Task<(ReadOnlyMemory<byte> Payload, string Signature)> CreateAsync(
        EventVectorAdmittedCall admitted, EventVectorRemoteReceiver receiver, EventVectorRemoteCallValidation authority,
        RemoteDocumentMac originalReplyMac, EventVectorRemoteEncoding encoding,
        CancellationToken originalCancellationToken)
        => CreateAsync(admitted, receiver, authority, originalReplyMac, encoding, null, originalCancellationToken);

    internal static async Task<(ReadOnlyMemory<byte> Payload, string Signature)> CreateAsync(
        EventVectorAdmittedCall admitted, EventVectorRemoteReceiver receiver, EventVectorRemoteCallValidation authority,
        RemoteDocumentMac originalReplyMac, EventVectorRemoteEncoding encoding,
        RemoteModelNativeEffectBoundary? boundary, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(originalReplyMac);
        ArgumentNullException.ThrowIfNull(encoding);
        var call = admitted.Original.Call;
        var work = receiver.CreateWork(admitted, originalCancellationToken);
        var result = ReadOnlyMemory<byte>.Empty;
        ErrorCode? errorCode = null;
        try
        {
            result = await receiver.ExecuteAsync(admitted, work, boundary, originalCancellationToken).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (boundary?.IsStarted == true)
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, EventVectorRemoteProtocol.Unavailable, error);
        }
        catch (KeyLoadException error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            errorCode = error.Code;
        }
        byte[]? originalBytes = null;
        var transferred = false;
        try
        {
            authority.Require(call, originalCancellationToken);
            var reply = new EventVectorRemoteReply(EventVectorRemoteProtocol.Version, call.RequestId,
                call.Nonce, EventVectorRemoteProtocol.Purpose, call.Action, receiver.Discovery(), result,
                errorCode, errorCode is null ? null : EventVectorRemoteProtocol.Unavailable);
            work.ChargeBytes(RemoteDocumentProtocol.SignatureCharacters);
            originalBytes = EventVectorWorkEncoding.Encode(reply, encoding, work);
            var signature = originalReplyMac.Sign(originalBytes, reply: true);
            authority.Require(call, originalCancellationToken);
            transferred = true;
            return (originalBytes, signature);
        }
        finally
        {
            if (!transferred && originalBytes is not null)
            {
                CryptographicOperations.ZeroMemory(originalBytes);
            }
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(result).Span);
        }
    }
}
