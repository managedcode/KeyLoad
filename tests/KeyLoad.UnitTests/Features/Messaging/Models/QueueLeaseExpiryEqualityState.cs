namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLeaseExpiryEqualityState(
    QueueLaneRef Lane, DateTimeOffset Boundary, CommandRequest Producer, OperationResult Produced,
    ReceiveRequest Claim, Delivery Original, IReadOnlyList<(DeliveryCommand Command, OperationResult Result)> Denied);
