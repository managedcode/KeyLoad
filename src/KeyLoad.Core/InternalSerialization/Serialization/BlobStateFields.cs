namespace KeyLoad.Core.Features.InternalSerialization;

internal static class BlobStateFields
{
    internal const uint FormatVersion = 0;
    internal const uint Incarnation = 1;
    internal const uint IntegrityIncarnation = 2;
    internal const uint BeginCommandId = 3;
    internal const uint CreatorPrincipalId = 4;
    internal const uint Blob = 5;
    internal const uint UploadId = 6;
    internal const uint Access = 7;
    internal const uint DeclaredLength = 8;
    internal const uint ExpectedRevision = 9;
    internal const uint ExpiresAt = 10;
    internal const uint NextOrdinal = 11;
    internal const uint StoredBytes = 12;
    internal const uint IntegrityHash = 13;
    internal const uint Status = 14;
    internal const uint RemainingReservation = 15;
    internal const uint ReclaimCursor = 16;
    internal const uint Retired = 17;
}
