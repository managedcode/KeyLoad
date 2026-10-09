namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Separately configured offline file-owner operation, never a snapshot-supplied role.</summary>
[ConfigurationOptions]
internal sealed class ClusterRestoreOperatorConfiguration
{
    public Guid OperationId { get; set; }
    public ClusterRestoreOperatorCredential[] Credentials { get; set; } = [];
    public string DestinationRoot { get; set; } = string.Empty;
    public ClusterRestoreOwnerSource[] Sources { get; set; } = [];
    public ClusterRestoreMappingConfiguration[] Mappings { get; set; } = [];
    public ClusterRestoreNodeTarget[] Nodes { get; set; } = [];
    public ClusterRestoreSigningIdentity[] SigningIdentities { get; set; } = [];
}
