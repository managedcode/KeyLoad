namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionPairedIdentityInventoryCut(Dictionary<string, byte[]?> Bytes,
    Dictionary<string, UnixFileMode> Modes);
