namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotInstallFencingProtocol
{
    internal const int OriginalCut = 3;
    internal const int SnapshotCut = 4;
    internal const int TailCut = 5;
    internal const int HealthyCut = 6;
    internal const long HealthyRevision = 5;
    internal const long PreviousRevision = 4;
    internal const long InitialTerm = 1;
    internal const long OriginalGeneration = 0;
    internal const long InstalledGeneration = 1;
    internal const int MaximumRows = 512;
    internal const int MaximumSnapshotFiles = 3;
    internal const int FirstOffset = 0;
    internal const int OneByte = 1;
    internal const string RowSeparator = ":";
    internal const string FilePattern = "*";
    internal const string InvalidInventory = "The real transfer fixture exceeded its closed native inventory.";
    internal const string HealthyJson = "{\"value\":\"transfer-fencing-healthy\"}";
}
