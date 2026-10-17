namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueAccountingOperation
{
    Ack,
    Nack,
    Processing,
    Reclaim,
    ReadyExpiry,
    ScheduledExpiry
}
