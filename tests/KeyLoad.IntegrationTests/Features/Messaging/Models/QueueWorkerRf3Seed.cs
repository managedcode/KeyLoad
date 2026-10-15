namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueWorkerRf3Seed(QueueLaneRef Lane, MessagingRf3Identity Identity,
    AtomicPartitionPlacementResolution Placement, CommandRequest Producer, CommitReceipt Receipt, byte[] SigningKey)
{
    public override string ToString() => "QueueWorkerRf3Seed(<private>)";
}
