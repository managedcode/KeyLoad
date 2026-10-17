namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueAccountingFaultState(QueueLaneRef Lane, DateTimeOffset Start,
    EnqueueMessage Message, CommandRequest Producer, OperationResult Produced,
    ReplicatedOperation Operation, QueueAccountingOperation Kind, QueueCounters Counters,
    MessageMetadata Metadata, long BodyBytes);
