namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobReclaimWholeFlowFixture(
    BlobRef Blob, Guid UploadId, AbortBlobUploadRequest Abort, BlobUploadInfo Aborted)
{
    internal const string Principal = "root";
    internal const string BlobId = "aborted-cold-reclaim";
    internal const int FirstOrdinal = 0;
    internal const int SecondOrdinal = 1;
    internal const int EmptyCount = 0;
    internal const int SingleCount = 1;
    internal const int InitialRevision = 0;
    internal const int AcceptedParts = 2;
    internal const int UnwrittenBytes = 17;
    internal const int PayloadModulus = 239;
    internal const int StoredBytes = AcceptedParts * BlobLimits.RawPartBytes;
    internal const int DeclaredBytes = StoredBytes + UnwrittenBytes;
    internal const int PublishedRevision = 1;
    internal const int RangeOffset = 1;
    internal const int RangeCount = 2;
    internal static readonly byte[] Payload = Enumerable.Range(FirstOrdinal, StoredBytes)
        .Select(static value => (byte)(value % PayloadModulus)).ToArray();
    internal static readonly byte[] Replacement = [0x31, 0x00, 0x73, 0xFF];
}
