namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueDeadLetterRf3State(PartitionRef Partition, CommandRequest Producer,
    CommitReceipt Produced, DeliveryCommand Nack, CommitReceipt Completed, CommandRequest Refused)
{
    internal QueueLaneRef Lane => new(Partition, QueueDeadLetterRf3Protocol.Queue);
    internal QueueLaneRef HealthyLane => new(Partition, QueueDeadLetterRf3Protocol.HealthyQueue);
}
