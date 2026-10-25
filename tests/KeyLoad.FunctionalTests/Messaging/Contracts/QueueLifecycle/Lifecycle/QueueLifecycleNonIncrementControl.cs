namespace KeyLoad.FunctionalTests.Messaging;

internal enum QueueLifecycleNonIncrementControl
{
    BlockRedriveKeepsOrder,
    ScheduledRedriveKeepsReady,
    FullDeadLetterKeepsParked
}
