namespace KeyLoad.Core.Messaging;

internal sealed record QueueDeadLetterAdmissionPlan(MessageMetadata Metadata, QueueCounters Counters, QueueParkOrderPlan Order);
