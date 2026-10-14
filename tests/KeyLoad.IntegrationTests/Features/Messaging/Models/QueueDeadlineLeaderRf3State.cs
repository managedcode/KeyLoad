namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDeadlineLeaderRf3State(QueueLaneRef lane, MessagingRf3Identity identity,
    DateTimeOffset due, AtomicPartitionPlacementResolution placement, OutboxStatus baseline)
{
    internal QueueLaneRef Lane { get; } = lane;
    internal MessagingRf3Identity Identity { get; } = identity;
    internal DateTimeOffset Due { get; } = due;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal OutboxStatus Baseline { get; } = baseline;
    internal MessageInspection? Original
    {
        get; set;
    }
    internal MessageInspection? Healthy
    {
        get; set;
    }
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Producers { get; } = [];
    internal List<(DeliveryCommand Command, CommitReceipt Receipt)> Acks { get; } = [];
}
