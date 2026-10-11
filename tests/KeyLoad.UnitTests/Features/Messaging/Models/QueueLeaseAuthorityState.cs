namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLeaseAuthorityState(QueueLaneRef Lane, DateTimeOffset Start,
    EnqueueMessage Message, CommandRequest Producer, OperationResult Produced,
    ReplicatedOperation Claim, OperationResult Claimed, Delivery Delivery,
    ReplicatedOperation Original, QueueLeaseAuthorityOperation Kind, MessageMetadata Metadata,
    QueueCounters Counters, long BodyBytes, byte[] FaultKey, byte[] OriginalBytes);
