namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobPersistedCorruptionFixture(
    BlobRef Blob,
    Guid UploadId,
    BlobRef HealthyBlob,
    BlobMetadata HealthyMetadata,
    byte[] HealthyBytes,
    bool Published);
