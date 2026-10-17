using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

// This owner records local encoding/MAC, never an observed network exchange.
internal sealed class EventVectorLocalExchange(OrleansNode node, EventVectorRemoteReceiver receiver,
    EventVectorRemoteCallValidation authority, EventVectorRemoteEncoding encoding,
    RemoteDocumentMac originalLocalMac, EventVectorReplayCache originalReplay)
{
    internal async Task<EventVectorAuthenticatedExchange> SendAsync(EventVectorRemoteCall originalCall,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        EventVectorLocalCallAuthority.Require(originalCall, authority, node, originalCancellationToken);
        if (originalCall.Action is not (EventVectorRemoteAction.Coverage or EventVectorRemoteAction.OriginalOutcome
            or EventVectorRemoteAction.ParentOutcome or EventVectorRemoteAction.FirstIssuanceOutcome
            or EventVectorRemoteAction.SourceRead or EventVectorRemoteAction.SourceEffect))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, EventVectorRemoteProtocol.Invalid);
        }
        work.ChargeBytes(RemoteDocumentProtocol.SignatureCharacters);
        var originalBytes = EventVectorWorkEncoding.Encode(originalCall, encoding, work);
        EventVectorRemoteCall? decoded = null;
        EventVectorMacCheckedReply? reply = null;
        var transferred = false;
        try
        {
            var signature = originalLocalMac.Sign(originalBytes, reply: false);
            if (!originalLocalMac.Verify(originalBytes, signature, reply: false))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
            }
            decoded = encoding.DecodeAuthenticatedCall(originalBytes);
            var admitted = EventVectorAdmittedCall.AdmitLocal(new(decoded, originalBytes, signature),
                authority, node, originalReplay, originalCancellationToken);
            reply = await EventVectorLocalReply.ExecuteAsync(receiver, authority, node, originalLocalMac,
                encoding, admitted, work, originalCancellationToken).ConfigureAwait(false);
            var result = new EventVectorAuthenticatedExchange(originalBytes, signature, reply);
            transferred = true;
            return result;
        }
        finally
        {
            if (decoded is not null)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(decoded.OriginalNativeRequest).Span);
            }
            if (!transferred)
            {
                CryptographicOperations.ZeroMemory(originalBytes);
                if (reply is not null)
                {
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(reply.OriginalReplyBytes).Span);
                    CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(reply.Reply.OriginalNativeReply).Span);
                }
            }
        }
    }
}
