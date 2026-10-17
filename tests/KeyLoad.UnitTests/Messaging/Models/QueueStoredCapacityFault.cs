namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueStoredCapacityFault
{
    ZeroUsageWithBodies,
    RecordsWithoutBytes,
    BytesWithoutRecords,
    UsageWithoutBodies
}

internal sealed record QueueStoredCapacityState(CommandRequest Producer, global::KeyLoad.OperationResult Original,
    DateTimeOffset ScheduledAt, global::KeyLoad.ReplicatedOperation Enqueue, global::KeyLoad.ReplicatedOperation Receive);

internal sealed record QueueStoredCapacityRow(byte[] Key, byte[] Original);
