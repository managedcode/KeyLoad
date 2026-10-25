namespace KeyLoad.FunctionalTests.Messaging;

internal enum QueueReadyAuthorityFault
{
    ScheduledState,
    ReadySequence,
    MetadataIdentity,
    BodyIdentity,
    NegativeInFlightMessages,
    NegativeInFlightBytes,
    ExcessInFlightMessages,
    ExcessInFlightBytes,
    NegativeReadySequence,
    NegativeOrderSequence,
    InFlightCountWithoutBytes,
    InFlightBytesWithoutCount,
    NegativeAttempts,
    NonPositiveStateVersion,
    NegativeLeaseVersion,
    NonPositiveDeliveryGeneration
}
