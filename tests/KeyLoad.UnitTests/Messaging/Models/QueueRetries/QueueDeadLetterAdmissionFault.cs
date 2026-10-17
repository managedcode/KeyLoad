namespace KeyLoad.UnitTests.Features.Messaging;

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
