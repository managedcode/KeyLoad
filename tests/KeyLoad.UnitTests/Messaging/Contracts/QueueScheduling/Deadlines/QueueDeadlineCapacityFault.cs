namespace KeyLoad.UnitTests.Messaging;

internal enum QueueDeadlineCapacityFault
{
    StateVersion,
    ReadySequence,
    ParkedSequence,
    FullDeadLetterMaxPark,
    ExpiryBeforeDeadLetterMaxPark
}
