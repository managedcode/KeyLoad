namespace KeyLoad.UnitTests.Features.TimeSeries;

internal enum SampleMetadataIntegrityFault
{
    RetentionFloor,
    RetentionCount,
    RetentionVersion,
    RetentionRoot,
    RetentionWire,
    BucketEndpoint,
    BucketCount,
    BucketSequence,
    BucketFloor,
    BucketVersion
}

internal sealed record SampleMetadataIntegrityCut(long Position, byte[] Identity, string Image);
