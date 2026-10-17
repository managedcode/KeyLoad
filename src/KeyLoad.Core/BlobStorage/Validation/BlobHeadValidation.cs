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
        const int RevisionValidationBoundary = 0;
        const int MetadataLengthEmptyCount = 0;

        if (metadata is null || metadata.Blob != blob || metadata.Access is null || metadata.Revision < RevisionValidationBoundary
            || metadata.Length is < MetadataLengthEmptyCount or > BlobLimits.MaximumBlobBytes
            || metadata.PartCount != BlobRecordReader.PartCount(metadata.Length))
        {
            throw BlobErrors.Corruption();
        }
    }

    private static void ValidatePublished(BlobMetadata metadata, Guid id)
    {
        const int EmptyRevision = 0;

        if (id == Guid.Empty || metadata.Revision == EmptyRevision || metadata.Deleted || metadata.IntegrityHash is null)
        {
            throw BlobErrors.Corruption();
        }
        BlobRecordReader.Hash(metadata.IntegrityHash);
    }

    private static void ValidateUnpublished(BlobMetadata metadata)
    {
        const int EmptyMetadataLength = 0;
        const int EmptyPartCount = 0;
        const int RevisionValidationBoundary = 0;

        if (metadata.Length != EmptyMetadataLength || metadata.PartCount != EmptyPartCount || metadata.IntegrityHash is not null
            || metadata.Revision > RevisionValidationBoundary && !metadata.Deleted)
        {
            throw BlobErrors.Corruption();
        }
    }
}
