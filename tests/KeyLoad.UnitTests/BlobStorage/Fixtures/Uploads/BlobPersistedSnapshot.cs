namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobPersistedSnapshot(
    byte[]? Head,
    byte[]? State,
    byte[]? Quota,
    byte[]? GlobalQuota,
    byte[]? Part,
    byte[]? PartMetadata,
    byte[]? Clock,
    long Position,
    long LastApplied);
