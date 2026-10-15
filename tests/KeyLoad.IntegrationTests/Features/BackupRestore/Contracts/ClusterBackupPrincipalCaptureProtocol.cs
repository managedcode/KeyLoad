namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterBackupPrincipalCaptureProtocol
{
    internal const string PrincipalPrefix = "backup-capture-principal-";
    internal const string KeyPrefix = "backup-principal-key-";
    internal const string Separator = ".";
    internal const string ArchiveRoot = "principal-capture-off-node";
    internal const string SdkDenied = "Cluster administration is required.";
    internal const string McpDenied = "The authenticated principal is not permitted to perform this operation.";
    internal const int SecretBytes = 32;
    internal const long InitialEpoch = 1;
    internal const long DeniedEpoch = 2;
    internal const long RepairedEpoch = 3;
    internal const long EmptyPosition = 0;
}
