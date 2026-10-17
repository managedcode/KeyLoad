namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueStoredCapacityState(CommandRequest Producer, global::KeyLoad.OperationResult Original,
    DateTimeOffset ScheduledAt, global::KeyLoad.ReplicatedOperation Enqueue, global::KeyLoad.ReplicatedOperation Receive);
