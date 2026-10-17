namespace KeyLoad.Core.Features.Messaging;

internal sealed record QueueDeadLetterAdmissionPlan(MessageMetadata Metadata, QueueCounters Counters, QueueParkOrderPlan Order);
