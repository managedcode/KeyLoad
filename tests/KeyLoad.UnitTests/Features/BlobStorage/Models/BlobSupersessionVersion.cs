namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobSupersessionVersion(BlobMetadata Metadata, BlobRangeRetainedOperation[] Commands);
