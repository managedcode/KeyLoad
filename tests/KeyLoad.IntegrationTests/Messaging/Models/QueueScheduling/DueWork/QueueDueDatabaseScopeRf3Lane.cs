namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDueDatabaseScopeRf3Lane(QueueLaneRef lane, string payload,
    AtomicPartitionPlacementResolution placement, OutboxStatus outbox)
{
    internal QueueLaneRef Lane { get; } = lane;
    internal string Payload { get; } = payload;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal OutboxStatus Outbox { get; } = outbox;
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
