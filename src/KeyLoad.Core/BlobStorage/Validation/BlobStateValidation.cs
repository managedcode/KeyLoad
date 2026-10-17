namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobStateValidation
{
    internal static void Validate(BlobState state, BlobRef blob, Guid upload, Guid authority)
    {
        const int EmptyNextOrdinal = 0;

        BlobRecordReader.Version(state.FormatVersion);
        if (state.Incarnation != authority)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, BlobErrors.Fenced);
        }
        ValidateIdentity(state, blob, upload);
        ValidateCounters(state);
        BlobMetadataRules.Access(state.Access, true);
        ValidateLifecycle(state);
        BlobRecordReader.Hash(state.IntegrityHash);
        if (state.NextOrdinal == EmptyNextOrdinal && !BlobIntegrity.Matches(state.IntegrityHash,
            BlobIntegrity.InitialHash(state.IntegrityIncarnation, blob, upload, state.DeclaredLength)))
        {
            throw BlobErrors.Corruption();
        }
    }

    private static void ValidateIdentity(BlobState state, BlobRef blob, Guid upload)
    {
        if (state.Blob != blob || state.UploadId != upload || upload == Guid.Empty
            || state.BeginCommandId == Guid.Empty || state.IntegrityIncarnation == Guid.Empty
            || string.IsNullOrEmpty(state.CreatorPrincipalId) || state.Access is null)
        {
            throw BlobErrors.Corruption();
        }
    }

    private static void ValidateCounters(BlobState state)
    {
        const int DeclaredLengthEmptyCount = 0;
        const int ExpectedRevisionValidationBoundary = 0;
        const int NextOrdinalValidationBoundary = 0;
        const int ReclaimCursorValidationBoundary = 0;
        const int StoredBytesValidationBoundary = 0;
        const int RemainingReservationValidationBoundary = 0;

        if (state.DeclaredLength is < DeclaredLengthEmptyCount or > BlobLimits.MaximumBlobBytes || state.ExpectedRevision < ExpectedRevisionValidationBoundary
            || !Enum.IsDefined(state.Status) || state.NextOrdinal < NextOrdinalValidationBoundary
            || state.NextOrdinal > BlobRecordReader.PartCount(state.DeclaredLength)
            || state.ReclaimCursor < ReclaimCursorValidationBoundary || state.ReclaimCursor > state.NextOrdinal || state.StoredBytes < StoredBytesValidationBoundary
            || state.RemainingReservation < RemainingReservationValidationBoundary || state.StoredBytes > state.DeclaredLength
            || state.RemainingReservation > state.DeclaredLength - state.StoredBytes)
        {
            throw BlobErrors.Corruption();
        }
    }

    private static void ValidateLifecycle(BlobState state)
    {
        const int EmptyReclaimCursor = 0;
        const int EmptyRemainingReservation = 0;

        var accepted = Math.Min(state.DeclaredLength, (long)state.NextOrdinal * BlobLimits.RawPartBytes);
        var deleted = Math.Min(state.DeclaredLength, (long)state.ReclaimCursor * BlobLimits.RawPartBytes);
        if (state.StoredBytes != accepted - deleted || state.Retired && state.Status != BlobUploadStatus.Complete
            || state.Status == BlobUploadStatus.Active && (state.ReclaimCursor != EmptyReclaimCursor || state.RemainingReservation != state.DeclaredLength - accepted)
            || state.Status != BlobUploadStatus.Active && state.RemainingReservation != EmptyRemainingReservation
            || state.Status == BlobUploadStatus.Complete && state.NextOrdinal != BlobRecordReader.PartCount(state.DeclaredLength))
        {
            throw BlobErrors.Corruption();
        }
    }
}
