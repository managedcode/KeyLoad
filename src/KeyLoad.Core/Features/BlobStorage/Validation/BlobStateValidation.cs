namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobStateValidation
{
    internal static void Validate(BlobState state, BlobRef blob, Guid upload, Guid authority)
    {
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
        if (state.NextOrdinal == 0 && !BlobIntegrity.Matches(state.IntegrityHash,
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
        if (state.DeclaredLength is < 0 or > BlobLimits.MaximumBlobBytes || state.ExpectedRevision < 0
            || !Enum.IsDefined(state.Status) || state.NextOrdinal < 0
            || state.NextOrdinal > BlobRecordReader.PartCount(state.DeclaredLength)
            || state.ReclaimCursor < 0 || state.ReclaimCursor > state.NextOrdinal || state.StoredBytes < 0
            || state.RemainingReservation < 0 || state.StoredBytes > state.DeclaredLength
            || state.RemainingReservation > state.DeclaredLength - state.StoredBytes)
        {
            throw BlobErrors.Corruption();
        }
    }

    private static void ValidateLifecycle(BlobState state)
    {
        var accepted = Math.Min(state.DeclaredLength, (long)state.NextOrdinal * BlobLimits.RawPartBytes);
        var deleted = Math.Min(state.DeclaredLength, (long)state.ReclaimCursor * BlobLimits.RawPartBytes);
        if (state.StoredBytes != accepted - deleted || state.Retired && state.Status != BlobUploadStatus.Complete
            || state.Status == BlobUploadStatus.Active && (state.ReclaimCursor != 0 || state.RemainingReservation != state.DeclaredLength - accepted)
            || state.Status != BlobUploadStatus.Active && state.RemainingReservation != 0
            || state.Status == BlobUploadStatus.Complete && state.NextOrdinal != BlobRecordReader.PartCount(state.DeclaredLength))
        {
            throw BlobErrors.Corruption();
        }
    }
}
