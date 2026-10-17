namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueAccountingHealthyState(CommandRequest Producer, OperationResult Produced,
    ReceiveRequest Claim, OperationResult Claimed, DeliveryCommand Ack, OperationResult Acked,
    QueueCounters Counters, MessageMetadata Metadata);
