namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueLifecycleQueueLifecycleIncrementCapacity operations against the caller's database context.</summary>
public static class MessagingValidationQueueLifecycleQueueLifecycleIncrementCapacity
{
    internal const string QueueLifecycleIncrementCapacityDetail = "The queue lifecycle version or sequence is exhausted.";
    internal static void RequireQueueLifecycleIdentityCapacity(MessageMetadata metadata)
    {
        if (metadata.StateVersion == long.MaxValue || metadata.DeliveryGeneration == long.MaxValue || metadata.LeaseVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacityDetail);
        }
    }

    internal static void RequireQueueRedriveReadyCapacity(QueueCounters counters, bool scheduled)
    {
        if (!scheduled && counters.NextReadySequence == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacityDetail);
        }
    }
}
