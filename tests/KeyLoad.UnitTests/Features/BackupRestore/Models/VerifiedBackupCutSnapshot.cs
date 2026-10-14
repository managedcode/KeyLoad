namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed record VerifiedBackupCutSnapshot(string Path, NativeCatalogBackupCapture Capture,
    string[] PartitionRows, IReadOnlyDictionary<string, byte[]> Archive);
