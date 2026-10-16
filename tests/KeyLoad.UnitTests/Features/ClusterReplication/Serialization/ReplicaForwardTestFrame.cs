using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>Frames genuine lower native Forward calls without creating authority or budgeting proof.</summary>
internal static class ReplicaForwardTestFrame
{
    internal static byte[] Payload(ReplicatedOperation operation)
        => WrapOperation(ReplicaProtocolCodec.Serialize(operation));

    internal static byte[] WrapOperation(ReadOnlyMemory<byte> originalOperation)
        => ReplicaProtocolCodec.Serialize(new ReplicaForwardRequestV1(
            ReplicaForwardMetadataProtocol.CurrentVersion, originalOperation, null, null));

    internal static ReplicatedOperation ReadOperation(ReadOnlyMemory<byte> payload)
    {
        var frame = ReplicaProtocolCodec.Deserialize<ReplicaForwardRequestV1>(payload.Span);
        ReplicaForwardFrameValidation.RequireRequest(frame);
        return ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(frame.Operation.Span);
    }

    internal static OperationResult ReadResult(ReadOnlyMemory<byte> payload)
    {
        var frame = ReplicaProtocolCodec.Deserialize<ReplicaForwardReplyV1>(payload.Span);
        ReplicaForwardFrameValidation.RequireReply(frame, budgeted: false);
        if (frame.Error is { } error)
        {
            throw Errors.Fail(error, frame.SafeDetail!);
        }

        return ReplicaProtocolCodec.Deserialize<OperationResult>(frame.Result.Span);
    }
}
