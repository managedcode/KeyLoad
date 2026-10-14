namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueDispatchCasRf3State(QueueLaneRef Lane, ResourceDefinition Definition,
    MessagingRf3Identity Member, CommandRequest Original, CommitReceipt Receipt,
    MessageInspection Message, byte[] Counters, byte[] Items,
    AtomicPartitionPlacementResolution Placement, OutboxStatus Outbox)
{
    internal Guid PauseId { get; } = Guid.NewGuid();
    internal Guid ResumeId { get; } = Guid.NewGuid();
    internal Guid StaleId { get; } = Guid.NewGuid();
    internal QueueDispatchControlRequest Request(long version, bool paused)
        => new(Lane.Partition.TenantId, Lane.Partition.DatabaseId, Lane.Queue, version, paused);
    internal ResourceDefinition Expected(long version, bool paused)
        => Definition with { SchemaVersion = version, Paused = paused };
}
