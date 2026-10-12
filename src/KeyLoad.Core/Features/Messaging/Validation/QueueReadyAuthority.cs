using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireQueueReadyAuthority(QueueLaneRef lane, string id,
        ReadOnlySpan<byte> actualKey, MessageMetadata metadata, MessageBody body)
    {
        if (metadata.Id != id || body.Id != id)
        { throw Errors.Fail(ErrorCode.Corruption, MissingReadyBody); }
        if (metadata.State != MessageState.Ready || metadata.ReadySequence < QueueReadyClaimsVersionOne
            || !actualKey.SequenceEqual(QueueKey(ReadyQueueSpace, lane, metadata.ReadySequence, id)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueRetryProtocol.StaleDecision); }
    }
}
