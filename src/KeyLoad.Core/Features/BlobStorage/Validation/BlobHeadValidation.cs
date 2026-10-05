namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobHeadValidation
{
    internal static void Validate(BlobHead head, BlobRef blob, Guid authority)
    {
        BlobRecordReader.Version(head.FormatVersion);
        var metadata = head.Metadata;
        if (head.Incarnation != authority)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, BlobErrors.Fenced);
        }
        ValidateMetadata(metadata, blob);
        BlobMetadataRules.Access(metadata.Access, true);
        if (metadata.VersionId is { } id)
        {
            ValidatePublished(metadata, id);
        }
        else
        {
            ValidateUnpublished(metadata);
        }
    }

    private static void ValidateMetadata(BlobMetadata metadata, BlobRef blob)
    {
        if (metadata is null || metadata.Blob != blob || metadata.Access is null || metadata.Revision < 0
            || metadata.Length is < 0 or > BlobLimits.MaximumBlobBytes
            || metadata.PartCount != BlobRecordReader.PartCount(metadata.Length))
        {
            throw BlobErrors.Corruption();
        }
    }

    private static void ValidatePublished(BlobMetadata metadata, Guid id)
    {
        if (id == Guid.Empty || metadata.Revision == 0 || metadata.Deleted || metadata.IntegrityHash is null)
        {
            throw BlobErrors.Corruption();
        }
        BlobRecordReader.Hash(metadata.IntegrityHash);
    }

    private static void ValidateUnpublished(BlobMetadata metadata)
    {
        if (metadata.Length != 0 || metadata.PartCount != 0 || metadata.IntegrityHash is not null
            || metadata.Revision > 0 && !metadata.Deleted)
        {
            throw BlobErrors.Corruption();
        }
    }
}
