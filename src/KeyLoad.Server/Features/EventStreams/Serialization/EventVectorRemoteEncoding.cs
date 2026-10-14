using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorRemoteEncoding(IOptions<DatabaseLimits> options)
{
    private const long FirstByte = 1;

    internal long MaximumBytes
    {
        get
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Value.Validate();
            return Math.Min(RemoteDocumentProtocol.MaximumBodyBytes,
                Math.Min(options.Value.MaxBatchBytes, options.Value.MaxQueryReadBytes));
        }
    }

    internal byte[] Encode<T>(T value)
        => RemoteDocumentWire.Encode(value, MaximumBytes);

    internal EventVectorRemoteCall DecodeAuthenticatedCall(ReadOnlySpan<byte> originalBytes)
    {
        RequireBytes(originalBytes.Length);
        var call = NativeSerialization.Deserialize<EventVectorRemoteCall>(originalBytes);
        if (call is null || call.Version != EventVectorRemoteProtocol.Version
            || call.Purpose != EventVectorRemoteProtocol.Purpose || !Enum.IsDefined(call.Action)
            || call.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(call.Nonce)
            || string.IsNullOrWhiteSpace(call.CallerVoter) || string.IsNullOrWhiteSpace(call.CallerSiloAddress)
            || call.Source is null || call.OriginalNativeRequest.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
        }
        RequireBytes(call.OriginalNativeRequest.Length);
        return call;
    }

    internal EventVectorRemoteReply DecodeAuthenticatedReply(ReadOnlySpan<byte> originalBytes,
        EventVectorRemoteCall originalCall)
    {
        ArgumentNullException.ThrowIfNull(originalCall);
        RequireBytes(originalBytes.Length);
        var reply = NativeSerialization.Deserialize<EventVectorRemoteReply>(originalBytes);
        if (reply is null || reply.Version != EventVectorRemoteProtocol.Version
            || reply.RequestId != originalCall.RequestId || reply.Nonce != originalCall.Nonce
            || reply.Purpose != EventVectorRemoteProtocol.Purpose || reply.Action != originalCall.Action
            || reply.ReceiverDiscovery is null || reply.Error is { } error && !Enum.IsDefined(error)
            || reply.Error is null && (reply.OriginalNativeReply.IsEmpty || reply.SafeDetail is not null)
            || reply.Error is not null && (!reply.OriginalNativeReply.IsEmpty || reply.SafeDetail is null)
            || reply.SafeDetail is not null && reply.SafeDetail != EventVectorRemoteProtocol.Invalid
                && reply.SafeDetail != EventVectorRemoteProtocol.Unavailable)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
        }
        if (!reply.OriginalNativeReply.IsEmpty)
        {
            RequireBytes(reply.OriginalNativeReply.Length);
        }
        return reply;
    }

    private void RequireBytes(long length)
    {
        if (length < FirstByte || length > MaximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, EventVectorRemoteProtocol.Invalid);
        }
    }
}
