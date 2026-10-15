namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string QueueLifecycleIncrementCapacityDetail = "The queue lifecycle version or sequence is exhausted.";

    private static void RequireQueueLifecycleIdentityCapacity(MessageMetadata metadata)
    {
        if (metadata.StateVersion == long.MaxValue || metadata.DeliveryGeneration == long.MaxValue
            || metadata.LeaseVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacityDetail);
        }
    }

    private static void RequireQueueRedriveReadyCapacity(QueueCounters counters, bool scheduled)
    {
        if (!scheduled && counters.NextReadySequence == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacityDetail);
        }
    }
}
