namespace KeyLoad.Core.Messaging;

internal sealed record QueueDueTransitionPlan(MessageMetadata Metadata, QueueCounters Counters,
    byte[]? OrderKeyToDelete, byte[]? AvailabilityKey, bool RemoveBody,
    QueueDeadLetterAdmissionPlan? DeadLetter);
