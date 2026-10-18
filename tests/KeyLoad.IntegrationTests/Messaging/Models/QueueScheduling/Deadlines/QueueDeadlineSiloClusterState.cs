namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDeadlineSiloClusterState(QueueLaneRef lane, MessagingClusterIdentity identity,
    DateTimeOffset due, OutboxStatus baseline)
{
    internal QueueLaneRef Lane { get; } = lane;
    internal MessagingClusterIdentity Identity { get; } = identity;
    internal DateTimeOffset Due { get; } = due;
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
