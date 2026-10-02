namespace KeyLoad.Core.Features.BlobStorage;

internal sealed record BlobHead(int FormatVersion, Guid Incarnation, BlobMetadata Metadata);
internal sealed record BlobState(int FormatVersion, Guid Incarnation, Guid IntegrityIncarnation,
    Guid BeginCommandId, string CreatorPrincipalId, BlobRef Blob, Guid UploadId, RowAccess Access,
    long DeclaredLength, long ExpectedRevision, DateTimeOffset ExpiresAt, int NextOrdinal,
    long StoredBytes, string IntegrityHash, BlobUploadStatus Status, long RemainingReservation,
    int ReclaimCursor, bool Retired = false)
{
    internal BlobUploadInfo Info => new(Blob, UploadId, DeclaredLength, ExpectedRevision, NextOrdinal,
        StoredBytes, ExpiresAt, Status, IntegrityHash);
    internal long ChargedBytes => checked(StoredBytes + RemainingReservation);
}
internal sealed record BlobPartMetadata(int FormatVersion, int Length, string Sha256);
internal sealed record BlobQuota(int FormatVersion, Guid Incarnation, long ReservedBytes,
    int ObjectKeys, int Versions, int Uploads)
{
    internal static BlobQuota Empty(Guid incarnation) => new(BlobKeys.FormatVersion, incarnation, 0, 0, 0, 0);
}

/// <summary>Binds a successful cached blob outcome to one immutable upload lifetime.</summary>
/// <param name="FormatVersion">The private authority stamp format.</param>
/// <param name="BeginCommandId">The command that created this upload lifetime.</param>
/// <param name="CreatorPrincipalId">The persisted principal that created this lifetime.</param>
public sealed record BlobOutcomeAuthority(int FormatVersion, Guid BeginCommandId, string CreatorPrincipalId);
