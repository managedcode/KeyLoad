namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Secret configuration remains operator memory and is never serialized or printed.</summary>
internal sealed class ClusterRestoreSigningIdentity
{
    public Guid SourceOwnerId
    {
        get; set;
    }
    public string SigningKey { get; set; } = string.Empty;
}
