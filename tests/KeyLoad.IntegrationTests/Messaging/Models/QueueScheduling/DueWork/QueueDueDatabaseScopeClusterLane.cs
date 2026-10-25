namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class QueueDueDatabaseScopeClusterLane(QueueLaneRef lane, string payload,
    OutboxStatus outbox)
{
    internal QueueLaneRef Lane { get; } = lane;
    internal string Payload { get; } = payload;
    internal OutboxStatus Outbox { get; set; } = outbox;
    internal MessageInspection? Message
    {
        get; set;
    }
    internal MessageInspection? Healthy
    {
        get; set;
    }
    internal List<(CommandRequest Command, CommitReceipt Result)> Seeds { get; } = [];
    internal List<(DeliveryCommand Command, CommitReceipt Result, long PolicyEpoch)> Acks { get; } = [];
}
