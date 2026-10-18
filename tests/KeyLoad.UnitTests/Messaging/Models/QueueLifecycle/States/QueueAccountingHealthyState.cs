using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueAccountingHealthyState(CommandRequest Producer, Result<object> Produced,
    ReceiveRequest Claim, Result<object> Claimed, DeliveryCommand Ack, Result<object> Acked,
    QueueCounters Counters, MessageMetadata Metadata);
