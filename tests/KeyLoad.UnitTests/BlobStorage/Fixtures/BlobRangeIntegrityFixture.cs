using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobRangeIntegrityFixture(BlobRef Blob, Guid UploadId, Guid BeginCommandId,
    DateTimeOffset BeganAt, BlobMetadata Metadata, ImmutableArray<BlobRangeRetainedOperation> Commands)
{
    internal const string Principal = "root";
    internal const string Resource = "range-blobs";
    internal const string BlobId = "cold-two-part-integrity";
    internal const string GuidFormat = "N";
    internal const string HeadSpace = "blob-head-v1";
    internal const string StateSpace = "blob-state-v1";
    internal const string PartSpace = "blob-part-v1";
    internal const string PartMetadataSpace = "blob-partmeta-v1";
    internal const string QuotaSpace = "blob-quota-v1";
    internal const string GlobalSpace = "blob-global-v1";
    internal const string ChainSpace = "blob-chain-v1";
    internal const string CorruptionDetail = "The persisted blob records are inconsistent.";
    internal const string UnsupportedDetail = "The blob record format is unsupported.";
    internal const int FormatVersion = 1;
    internal const int UnsupportedVersion = 99;
    internal const int FirstOrdinal = 0;
    internal const int SecondOrdinal = 1;
    internal const int Parts = 2;
    internal const int RawPartBytes = 65_536;
    internal const int TailBytes = 17;
    internal const int PayloadBytes = RawPartBytes + TailBytes;
    internal const int PayloadModulus = 251;
    internal const int InitialRevision = 0;
    internal const int Revision = 1;
    internal const int EmptyCount = 0;
    internal const int SingleCount = 1;
    internal const int DefaultUploadTtlSeconds = 3_600;
    internal const int CrossOffset = 1;
    internal const byte DamagedByte = 0xFF;
    internal static readonly byte[] Payload = Enumerable.Range(FirstOrdinal, PayloadBytes)
        .Select(static value => (byte)(value % PayloadModulus)).ToArray();
}
