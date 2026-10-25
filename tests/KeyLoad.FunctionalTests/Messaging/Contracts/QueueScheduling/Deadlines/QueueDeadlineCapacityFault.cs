namespace KeyLoad.FunctionalTests.Messaging;

internal enum QueueDeadlineCapacityFault
{
    StateVersion,
    ReadySequence,
    ParkedSequence,
    FullDeadLetterMaxPark,
    ExpiryBeforeDeadLetterMaxPark
}
