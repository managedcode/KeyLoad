using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueRetryColdState(QueueLaneRef Lane, DateTimeOffset Start, CommandRequest Enqueue,
    Result<object> Enqueued, ReceiveRequest FirstClaim, Result<object> Claimed, Delivery FirstDelivery)
{
    internal List<(DeliveryCommand Command, Result<object> Result)> Completed { get; } = [];
    internal (CommandRequest Command, Result<object> Result)? Healthy
    {
        get; set;
    }
    internal (CommandRequest Command, Result<object> Result)? Rejected
    {
        get; set;
    }
    internal DateTimeOffset FinalTime => Start.AddSeconds(QueueRetryColdProtocol.AfterExpirySeconds);
}
