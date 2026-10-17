namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLeaseAuthorityHealthyState(CommandRequest Producer, OperationResult Produced,
    ReplicatedOperation Claim, OperationResult Claimed, ReplicatedOperation Ack, OperationResult Acked,
    QueueCounters Counters, MessageMetadata Metadata);
