using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLeaseExpiryEqualityState(
    QueueLaneRef Lane, DateTimeOffset Boundary, CommandRequest Producer, Result<object> Produced,
    ReceiveRequest Claim, Delivery Original, IReadOnlyList<(DeliveryCommand Command, Result<object> Result)> Denied);
