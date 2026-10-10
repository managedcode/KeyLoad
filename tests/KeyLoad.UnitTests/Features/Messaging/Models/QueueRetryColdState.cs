namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueRetryColdState(QueueLaneRef Lane, DateTimeOffset Start, CommandRequest Enqueue,
    OperationResult Enqueued, ReceiveRequest FirstClaim, OperationResult Claimed, Delivery FirstDelivery)
{
    internal List<(DeliveryCommand Command, OperationResult Result)> Completed { get; } = [];
    internal (CommandRequest Command, OperationResult Result)? Healthy { get; set; }
    internal (CommandRequest Command, OperationResult Result)? Rejected { get; set; }
    internal DateTimeOffset FinalTime => Start.AddSeconds(QueueRetryColdProtocol.AfterExpirySeconds);
}
