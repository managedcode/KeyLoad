namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueDeadlineCapacityFault
{
    StateVersion,
    ReadySequence,
    ParkedSequence,
    FullDeadLetterMaxPark,
    ExpiryBeforeDeadLetterMaxPark
}
