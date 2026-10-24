namespace KeyLoad.Core.Messaging;

internal sealed record QueueLifecycleRemovalPlan(byte[] PrimaryKey, byte[]? SecondaryKey, QueueCounters Counters);
