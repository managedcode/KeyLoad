namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Explicit per-owner operator credential, never snapshot authority or diagnostic output.</summary>
internal sealed class ClusterRestoreOperatorCredential
{
    public Guid SourceOwnerId
    {
        get; set;
    }
    public string Credential { get; set; } = string.Empty;
}
