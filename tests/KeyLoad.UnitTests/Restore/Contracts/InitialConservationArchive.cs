using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed record InitialConservationArchive(string Directory, NativeCatalogBackupCapture Capture,
    ClusterBackupOwnerCut Cut, StoreIdentity Identity, string[] SourceRows,
    Dictionary<string, byte[]> Files);
