namespace KeyLoad.Core.Features.BlobStorage;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobHead)]
internal sealed record BlobHead(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobHeadFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobHeadFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobHeadFields.Metadata)] BlobMetadata Metadata);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobState)]
internal sealed record BlobState(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.IntegrityIncarnation)] Guid IntegrityIncarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.BeginCommandId)] Guid BeginCommandId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.CreatorPrincipalId)] string CreatorPrincipalId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.Blob)] BlobRef Blob,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.UploadId)] Guid UploadId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.Access)] RowAccess Access,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.DeclaredLength)] long DeclaredLength,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.ExpectedRevision)] long ExpectedRevision,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.ExpiresAt)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.NextOrdinal)] int NextOrdinal,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.StoredBytes)] long StoredBytes,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.IntegrityHash)] string IntegrityHash,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.Status)] BlobUploadStatus Status,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.RemainingReservation)] long RemainingReservation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.ReclaimCursor)] int ReclaimCursor,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobStateFields.Retired)] bool Retired = false)
{
    internal BlobUploadInfo Info => new(Blob, UploadId, DeclaredLength, ExpectedRevision, NextOrdinal,
        StoredBytes, ExpiresAt, Status, IntegrityHash);
    internal long ChargedBytes => checked(StoredBytes + RemainingReservation);
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobPartMetadata)]
internal sealed record BlobPartMetadata(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobPartMetadataFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobPartMetadataFields.Length)] int Length,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobPartMetadataFields.Sha256)] string Sha256);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobQuota)]
internal sealed record BlobQuota(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.Incarnation)] Guid Incarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.ReservedBytes)] long ReservedBytes,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.ObjectKeys)] int ObjectKeys,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.Versions)] int Versions,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobQuotaFields.Uploads)] int Uploads)
{
    private const int EmptyReservedBytes = 0;
    private const int EmptyObjectKeys = 0;
    private const int EmptyVersions = 0;
    private const int EmptyUploads = 0;

    internal static BlobQuota Empty(Guid incarnation) => new(BlobKeys.FormatVersion, incarnation, EmptyReservedBytes, EmptyObjectKeys, EmptyVersions, EmptyUploads);
}

/// <summary>Binds a successful cached blob outcome to one immutable upload lifetime.</summary>
/// <param name="FormatVersion">The private authority stamp format.</param>
/// <param name="BeginCommandId">The command that created this upload lifetime.</param>
/// <param name="CreatorPrincipalId">The persisted principal that created this lifetime.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobOutcomeAuthority)]
public sealed record BlobOutcomeAuthority(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobOutcomeAuthorityFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobOutcomeAuthorityFields.BeginCommandId)] Guid BeginCommandId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobOutcomeAuthorityFields.CreatorPrincipalId)] string CreatorPrincipalId);
