namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed record PartitionIdentityInventoryCut(Dictionary<string, byte[]?> Bytes,
    Dictionary<string, UnixFileMode> Modes);
