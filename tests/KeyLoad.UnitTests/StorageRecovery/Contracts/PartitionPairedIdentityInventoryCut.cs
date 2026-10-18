namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionIdentityInventoryCut(Dictionary<string, byte[]?> Bytes,
    Dictionary<string, UnixFileMode> Modes);
