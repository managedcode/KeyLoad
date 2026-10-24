namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueDeadLetterClusterState(PartitionRef Partition, CommandRequest Producer,
    CommitReceipt Produced, DeliveryCommand Nack, CommitReceipt Completed, CommandRequest Refused)
{
    internal QueueLaneRef Lane => new(Partition, QueueDeadLetterClusterProtocol.Queue);
    internal QueueLaneRef HealthyLane => new(Partition, QueueDeadLetterClusterProtocol.HealthyQueue);
}
