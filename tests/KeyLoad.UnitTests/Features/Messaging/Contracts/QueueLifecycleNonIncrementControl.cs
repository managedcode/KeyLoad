namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueLifecycleNonIncrementControl
{
    BlockRedriveKeepsOrder,
    ScheduledRedriveKeepsReady,
    FullDeadLetterKeepsParked
}
