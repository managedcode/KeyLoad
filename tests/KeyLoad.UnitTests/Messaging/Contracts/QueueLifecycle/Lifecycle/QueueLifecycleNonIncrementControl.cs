namespace KeyLoad.UnitTests.Messaging;

internal enum QueueLifecycleNonIncrementControl
{
    BlockRedriveKeepsOrder,
    ScheduledRedriveKeepsReady,
    FullDeadLetterKeepsParked
}
