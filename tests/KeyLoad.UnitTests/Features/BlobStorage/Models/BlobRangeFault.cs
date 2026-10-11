namespace KeyLoad.UnitTests.Features.BlobStorage;

internal enum BlobRangeFault
{
    MissingRaw,
    TruncatedRaw,
    ChangedUnrequestedByte,
    UnsupportedPartMetadata
}
