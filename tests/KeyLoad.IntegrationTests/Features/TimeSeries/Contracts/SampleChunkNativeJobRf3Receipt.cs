namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkNativeJobRf3Receipt(Guid CommandId, string JobId, string MetadataDigest);
