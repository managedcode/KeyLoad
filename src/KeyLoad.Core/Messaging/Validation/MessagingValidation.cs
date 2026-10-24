using KeyLoad.Core.Messaging;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidation operations against the caller's database context.</summary>
public static class MessagingValidation
{
    internal static void RequireQueueReadyAuthority(QueueLaneRef lane, string id, ReadOnlySpan<byte> actualKey, MessageMetadata metadata, MessageBody body)
    {
        if (metadata.Id != id || body.Id != id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueReadyClaims.MissingReadyBody);
        }

        if (metadata.State != MessageState.Ready || metadata.ReadySequence < QueueReadyClaims.QueueReadyClaimsVersionOne || metadata.Attempts < QueueRetryProtocol.Initial || metadata.StateVersion < QueueReadyClaims.QueueReadyClaimsVersionOne || metadata.LeaseVersion < QueueRetryProtocol.Initial || metadata.DeliveryGeneration < QueueReadyClaims.QueueReadyClaimsVersionOne || !actualKey.SequenceEqual(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.ReadyQueueSpace, lane, metadata.ReadySequence, id)))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueRetryProtocol.StaleDecision);
        }
    }
}
