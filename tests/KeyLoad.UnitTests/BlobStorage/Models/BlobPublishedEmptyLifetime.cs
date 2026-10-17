namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobPublishedEmptyLifetime(BlobMetadata Metadata, BlobUploadInfo Active,
    BlobRangeRetainedOperation Begin, BlobRangeRetainedOperation Complete);
