namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeadlineCapacityState(QueueLaneRef Lane, DateTimeOffset Start, DateTimeOffset Due,
    EnqueueMessage Input, MessageMetadata Original, QueueCounters Counters, bool Sweep, bool Jitter,
    QueueDeadlineKind Kind, QueueDeadlineCapacityFault Fault, byte[] FaultKey, byte[] OriginalBytes,
    ReplicatedOperation Attempt, List<(ReplicatedOperation Operation, OperationResult Result)> Outcomes);
