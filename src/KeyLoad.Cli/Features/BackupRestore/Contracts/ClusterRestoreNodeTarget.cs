namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Nonsecret separately configured node directory binding.</summary>
internal sealed record ClusterRestoreNodeTarget(Guid SourceOwnerId, string VoterId, string RelativeDataDirectory);

/// <summary>Original immutable owner archive selected by the configured operator.</summary>
internal sealed record ClusterRestoreOwnerSource(Guid OwnerId, string ArchiveDirectory, string ExpectedManifestDigest);

/// <summary>Secret configuration remains operator memory and is never serialized or printed.</summary>
internal sealed class ClusterRestoreSigningIdentity
{
    public Guid SourceOwnerId { get; set; }
    public string SigningKey { get; set; } = string.Empty;
}

/// <summary>Explicit per-owner operator credential, never snapshot authority or diagnostic output.</summary>
internal sealed class ClusterRestoreOperatorCredential
{
    public Guid SourceOwnerId { get; set; }
    public string Credential { get; set; } = string.Empty;
}
