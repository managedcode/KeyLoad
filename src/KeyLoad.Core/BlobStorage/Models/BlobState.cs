namespace KeyLoad.Core.Features.BlobStorage;

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
