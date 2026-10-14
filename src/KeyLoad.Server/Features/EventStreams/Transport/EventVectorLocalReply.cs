using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorLocalReply
{
    internal static async Task<EventVectorMacCheckedReply> ExecuteAsync(EventVectorRemoteReceiver receiver,
        EventVectorRemoteCallValidation authority, OrleansNode node, RemoteDocumentMac originalLocalMac,
        EventVectorRemoteEncoding encoding, EventVectorAdmittedCall admitted, ReadExecutionBudget work,
        CancellationToken originalCancellationToken)
    {
        var call = admitted.Original.Call;
        var nativeResult = await receiver.ExecuteAsync(admitted, work,
            originalCancellationToken).ConfigureAwait(false);
        byte[]? replyBytes = null;
        EventVectorRemoteReply? decoded = null;
        var transferred = false;
        try
        {
            EventVectorLocalCallAuthority.Require(call, authority, node, originalCancellationToken);
            var actualReply = new EventVectorRemoteReply(EventVectorRemoteProtocol.Version, call.RequestId,
                call.Nonce, EventVectorRemoteProtocol.Purpose, call.Action, receiver.Discovery(), nativeResult, null, null);
            work.ChargeBytes(RemoteDocumentProtocol.SignatureCharacters);
            replyBytes = EventVectorWorkEncoding.Encode(actualReply, encoding, work);
            var signature = originalLocalMac.Sign(replyBytes, reply: true);
            if (!originalLocalMac.Verify(replyBytes, signature, reply: true))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
            }
            decoded = encoding.DecodeAuthenticatedReply(replyBytes, call);
            EventVectorLocalCallAuthority.Require(call, authority, node, originalCancellationToken);
            work.Check();
            var result = new EventVectorMacCheckedReply(decoded, replyBytes, signature);
            transferred = true;
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(nativeResult).Span);
            if (!transferred)
            {
                if (replyBytes is not null)
                {
                    CryptographicOperations.ZeroMemory(replyBytes);
                }
                if (decoded is not null)
                {
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(decoded.OriginalNativeReply).Span);
                }
            }
        }
    }
}
