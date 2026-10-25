namespace KeyLoad.FunctionalTests.Messaging;

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
