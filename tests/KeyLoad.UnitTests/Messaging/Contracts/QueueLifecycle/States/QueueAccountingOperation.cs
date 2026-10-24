namespace KeyLoad.UnitTests.Messaging;

internal enum QueueAccountingOperation
{
    Ack,
    Nack,
    Processing,
    Reclaim,
    ReadyExpiry,
    ScheduledExpiry
}
