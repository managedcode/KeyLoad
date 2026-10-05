using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPersistedHeadMutation
{
    private const int UnsupportedFormatVersion = 99;
    private const long ExcessiveLength = long.MaxValue;
    private const string InvalidHash = "not-a-sha256-chain";

    internal static BlobHead Apply(BlobHead head, BlobPersistedHeadCase testCase, BlobRef blob,
        Guid incarnation)
    {
        var metadata = head.Metadata;
        metadata = testCase switch
        {
            BlobPersistedHeadCase.BlobIdentity => metadata with { Blob = metadata.Blob with { Id = "different-blob" } },
            BlobPersistedHeadCase.NegativeRevision => metadata with { Revision = -1 },
            BlobPersistedHeadCase.NegativeLength => metadata with { Length = -1 },
            BlobPersistedHeadCase.ExcessiveLength => metadata with { Length = ExcessiveLength },
            BlobPersistedHeadCase.PartCount => metadata with { PartCount = metadata.PartCount + 1 },
            BlobPersistedHeadCase.PublishedEmptyVersion => metadata with { VersionId = Guid.Empty },
            BlobPersistedHeadCase.PublishedZeroRevision => metadata with { Revision = 0 },
            BlobPersistedHeadCase.PublishedDeleted => metadata with { Deleted = true },
            BlobPersistedHeadCase.PublishedMissingHash => metadata with { IntegrityHash = null },
            BlobPersistedHeadCase.PublishedInvalidHash => metadata with { IntegrityHash = InvalidHash },
            BlobPersistedHeadCase.MissingAccess => metadata with { Access = null! },
            BlobPersistedHeadCase.InvalidAccess => metadata with { Access = new("invalid owner!", null) },
            BlobPersistedHeadCase.UnpublishedLength => metadata with { Length = 1 },
            BlobPersistedHeadCase.UnpublishedPartCount => metadata with { PartCount = 1 },
            BlobPersistedHeadCase.UnpublishedHash => metadata with { IntegrityHash = InvalidHash },
            BlobPersistedHeadCase.UnpublishedLiveRevision => metadata with { Revision = 1 },
            _ => metadata
        };
        var formatVersion = testCase == BlobPersistedHeadCase.UnsupportedFormat
            ? UnsupportedFormatVersion : head.FormatVersion;
        var headIncarnation = testCase == BlobPersistedHeadCase.FencedIncarnation
            ? Different(incarnation) : head.Incarnation;
        return head with { FormatVersion = formatVersion, Incarnation = headIncarnation, Metadata = metadata };
    }

    private static Guid Different(Guid value) => value == Guid.Empty ? Guid.NewGuid() : Guid.Empty;
}
