namespace KeyLoad.UnitTests.Messaging;

internal enum QueueDeadLetterAdmissionFault
{
    MissingCounters,
    NegativeStoredMessages,
    NegativeDeadLetterMessages,
    NegativeNextParkedSequence,
    ExcessiveDeadLetterBytes,
    WitnessWithoutUsage,
    MissingWitnessMetadata
}
