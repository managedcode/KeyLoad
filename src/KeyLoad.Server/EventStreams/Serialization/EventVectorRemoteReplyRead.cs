using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteReplyRead
{
    internal static EventVectorMacCheckedReply Read(ReadOnlyMemory<byte> payload, string signature,
        EventVectorRemoteCall originalCall, RemoteDocumentMac actualReceiverMac,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalCall);
        ArgumentNullException.ThrowIfNull(actualReceiverMac);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(work);
        originalCancellationToken.ThrowIfCancellationRequested();
        if (payload.IsEmpty || payload.Length > encoding.MaximumBytes
            || signature is not { Length: RemoteDocumentProtocol.SignatureCharacters })
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
        }
        work.ChargeBytes(checked(payload.Length + Encoding.UTF8.GetByteCount(signature)));
        var originalBytes = payload.ToArray();
        var transferred = false;
        try
        {
            if (!actualReceiverMac.Verify(originalBytes, signature, reply: true))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
            }
            var reply = encoding.DecodeAuthenticatedReply(originalBytes, originalCall);
            originalCancellationToken.ThrowIfCancellationRequested();
            var result = new EventVectorMacCheckedReply(reply, originalBytes, signature);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                CryptographicOperations.ZeroMemory(originalBytes);
            }
        }
    }
}
