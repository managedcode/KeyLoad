namespace KeyLoad.UnitTests.Features.BlobStorage;

internal enum BlobPersistedHeadCase
{
    BlobIdentity,
    NegativeRevision,
    NegativeLength,
    ExcessiveLength,
    PartCount,
    FencedIncarnation,
    UnsupportedFormat,
    PublishedEmptyVersion,
    PublishedZeroRevision,
    PublishedDeleted,
    PublishedMissingHash,
    PublishedInvalidHash,
    MissingAccess,
    InvalidAccess,
    UnpublishedLength,
    UnpublishedPartCount,
    UnpublishedHash,
    UnpublishedLiveRevision
}
