namespace KeyLoad.FunctionalTests.Messaging;

internal enum QueueAccountingOperation
{
    Ack,
    Nack,
    Processing,
    Reclaim,
    ReadyExpiry,
    ScheduledExpiry
}
