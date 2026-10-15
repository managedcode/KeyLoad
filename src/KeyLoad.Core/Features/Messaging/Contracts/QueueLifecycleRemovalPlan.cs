namespace KeyLoad.Core.Features.Messaging;

internal sealed record QueueLifecycleRemovalPlan(byte[] PrimaryKey, byte[]? SecondaryKey, QueueCounters Counters);
