using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobPersistedStateMutation
{
    private const int ExcessivePartCount = int.MaxValue;
    private const int InvalidStatus = int.MaxValue;
    private const long ExcessiveLength = long.MaxValue;
    private const string InvalidHash = "not-a-sha256-chain";
    private const string InvalidOwnerId = "owner\u0001";

    internal static BlobState Apply(BlobState state, BlobPersistedStateCase testCase, BlobRef blob,
        Guid uploadId, Guid incarnation)
        => testCase switch
        {
            BlobPersistedStateCase.BlobIdentity => state with { Blob = state.Blob with { Id = "different-blob" } },
            BlobPersistedStateCase.UploadIdentity => state with { UploadId = Guid.NewGuid() },
            BlobPersistedStateCase.EmptyUploadIdentity => state with { UploadId = Guid.Empty },
            BlobPersistedStateCase.EmptyBeginCommand => state with { BeginCommandId = Guid.Empty },
            BlobPersistedStateCase.EmptyIntegrityIncarnation => state with { IntegrityIncarnation = Guid.Empty },
            BlobPersistedStateCase.EmptyCreator => state with { CreatorPrincipalId = string.Empty },
            BlobPersistedStateCase.MissingAccess => state with { Access = null! },
            BlobPersistedStateCase.InvalidAccess => state with { Access = new(InvalidOwnerId, null) },
            BlobPersistedStateCase.NegativeLength => state with { DeclaredLength = -1 },
            BlobPersistedStateCase.ExcessiveLength => state with { DeclaredLength = ExcessiveLength },
            BlobPersistedStateCase.NegativeRevision => state with { ExpectedRevision = -1 },
            BlobPersistedStateCase.InvalidStatus => state with { Status = (BlobUploadStatus)InvalidStatus },
            BlobPersistedStateCase.NegativeNextOrdinal => state with { NextOrdinal = -1 },
            BlobPersistedStateCase.ExcessiveNextOrdinal => state with { NextOrdinal = ExcessivePartCount },
            BlobPersistedStateCase.NegativeReclaimCursor => state with { ReclaimCursor = -1 },
            BlobPersistedStateCase.ExcessiveReclaimCursor => state with { ReclaimCursor = state.NextOrdinal + 1 },
            BlobPersistedStateCase.NegativeStoredBytes => state with { StoredBytes = -1 },
            BlobPersistedStateCase.ExcessiveStoredBytes => state with { StoredBytes = state.DeclaredLength + 1 },
            BlobPersistedStateCase.NegativeReservation => state with { RemainingReservation = -1 },
            BlobPersistedStateCase.ExcessiveReservation => state with { RemainingReservation = state.DeclaredLength },
            BlobPersistedStateCase.StoredByteEquation => state with { StoredBytes = state.StoredBytes - 1 },
            BlobPersistedStateCase.RetiredActive => state with { Retired = true },
            BlobPersistedStateCase.ActiveReclaimed => state with { ReclaimCursor = 1, StoredBytes = 0 },
            BlobPersistedStateCase.ActiveReservation => state with { RemainingReservation = 1 },
            BlobPersistedStateCase.InactiveReservation => state with { Status = BlobUploadStatus.Aborted, RemainingReservation = 1 },
            BlobPersistedStateCase.CompletePartCount => state with
            {
                NextOrdinal = 0,
                StoredBytes = 0,
                IntegrityHash = BlobIntegrity.InitialHash(state.IntegrityIncarnation, blob, uploadId, state.DeclaredLength)
            },
            BlobPersistedStateCase.InvalidHash => state with { IntegrityHash = InvalidHash },
            BlobPersistedStateCase.InitialHashMismatch => state with
            {
                IntegrityHash = BlobIntegrity.InitialHash(incarnation, blob, Guid.NewGuid(), state.DeclaredLength)
            },
            BlobPersistedStateCase.FencedIncarnation => state with { Incarnation = Different(incarnation) },
            _ => throw new ArgumentOutOfRangeException(nameof(testCase), testCase, null)
        };

    internal static BlobQuota Apply(BlobQuota quota, BlobPersistedStateCase testCase)
        => testCase switch
        {
            BlobPersistedStateCase.MissingQuotaObject => quota with
            {
                ObjectKeys = 0,
                Versions = 0,
                ReservedBytes = 0,
                Uploads = 0
            },
            BlobPersistedStateCase.MissingQuotaVersion => quota with
            {
                Versions = 0,
                ReservedBytes = 0,
                Uploads = 0
            },
            BlobPersistedStateCase.MissingQuotaReservation => quota with { ReservedBytes = 0 },
            BlobPersistedStateCase.MissingQuotaUpload => quota with { Uploads = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(testCase), testCase, null)
        };

    internal static bool IsQuotaCase(BlobPersistedStateCase testCase)
        => testCase is BlobPersistedStateCase.MissingQuotaObject or BlobPersistedStateCase.MissingQuotaVersion
            or BlobPersistedStateCase.MissingQuotaReservation or BlobPersistedStateCase.MissingQuotaUpload;

    private static Guid Different(Guid value) => value == Guid.Empty ? Guid.NewGuid() : Guid.Empty;
}
