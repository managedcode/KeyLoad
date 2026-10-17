using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3Protocol
{
    internal const string ArtifactExtension = ".mcb";
    internal const int CurrentVersion = 1;
    internal const long InitialOwnerEpoch = 1;
    internal const int FailedExit = 1;
    internal const int SuccessfulExit = 0;
    internal const int ArtifactPieceBytes = 1_024;
    internal const string CaptureTool = "keyload_admin_cluster_backup";
    internal const string ArchiveDirectory = "cluster-backups";
    internal const string RestoreResource = "cluster-restore-operator";
    internal const string DatabaseDirectory = "database";
    internal const string Invalid = "The actual RF3 restore fixture violated its configured native owner or archive contract.";
    internal const string CliCommand = "restore-cluster";
    internal const string CliDll = "src/KeyLoad.Cli/bin/Release/net10.0/KeyLoad.Cli.dll";
    internal const string IdentityFormat = "D";
    internal const string ArchiveFormat = "N";
    internal static TimeSpan ParentDeadline => NodeEpochRf3Protocol.ParentDeadline;
    internal static string[] Nodes => TwoRf3MembershipProtocol.Nodes;
}
