namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueLeaseAuthorityFault
{
    MissingReference,
    ForeignReference,
    ForeignBodyIdentity,
    ForeignMetadataIdentity,
    MissingMetadataDeadline,
    NegativeAttempts,
    ZeroAttempts,
    NegativeStateVersion,
    ZeroStateVersion,
    UnclaimedStateVersion,
    NegativeReadySequence,
    ZeroReadySequence
}
