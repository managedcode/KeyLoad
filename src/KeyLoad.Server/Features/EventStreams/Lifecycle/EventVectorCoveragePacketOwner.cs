using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorCoveragePacketOwner : IDisposable
{
    private const int Version = 1;
    private byte[] request = [];
    private byte[] requestSignature = [];
    private byte[] reply = [];
    private byte[] replySignature = [];
    private byte[] capture = [];
    private EventVectorCoveragePacket? packet;

    private EventVectorCoveragePacketOwner() { }

    internal EventVectorCoveragePacket OriginalPacket => packet
        ?? throw new ObjectDisposedException(nameof(EventVectorCoveragePacketOwner));

    internal static EventVectorCoveragePacketOwner Create(EventVectorAuthenticatedExchange original,
        ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(original);
        var actualReply = original.Reply;
        if (actualReply.Reply.Action != EventVectorRemoteAction.Coverage
            || actualReply.Reply.Error is not null || actualReply.Reply.OriginalNativeReply.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid); }
        var requestSignatureLength = Encoding.UTF8.GetByteCount(original.OriginalRequestSignature);
        var replySignatureLength = Encoding.UTF8.GetByteCount(actualReply.OriginalSignature);
        work.ChargeBytes(checked((long)original.OriginalRequestBytes.Length + requestSignatureLength
            + actualReply.OriginalReplyBytes.Length + replySignatureLength
            + actualReply.Reply.OriginalNativeReply.Length));
        var owner = new EventVectorCoveragePacketOwner();
        var transferred = false;
        try
        {
            owner.request = original.OriginalRequestBytes.ToArray();
            owner.requestSignature = Encoding.UTF8.GetBytes(original.OriginalRequestSignature);
            owner.reply = actualReply.OriginalReplyBytes.ToArray();
            owner.replySignature = Encoding.UTF8.GetBytes(actualReply.OriginalSignature);
            owner.capture = actualReply.Reply.OriginalNativeReply.ToArray();
            owner.packet = new()
            {
                Version = Version,
                RequestBytes = owner.request,
                RequestSignature = owner.requestSignature,
                ReplyBytes = owner.reply,
                ReplySignature = owner.replySignature,
                OriginalCaptureBytes = owner.capture
            };
            transferred = true;
            return owner;
        }
        finally
        {
            if (!transferred)
            { owner.Dispose(); }
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(request);
        CryptographicOperations.ZeroMemory(requestSignature);
        CryptographicOperations.ZeroMemory(reply);
        CryptographicOperations.ZeroMemory(replySignature);
        CryptographicOperations.ZeroMemory(capture);
        request = [];
        requestSignature = [];
        reply = [];
        replySignature = [];
        capture = [];
        packet = null;
    }
}
