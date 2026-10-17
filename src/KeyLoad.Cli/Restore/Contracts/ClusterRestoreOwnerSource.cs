namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Original immutable owner archive selected by the configured operator.</summary>
internal sealed record ClusterRestoreOwnerSource(Guid OwnerId, string ArchiveDirectory, string ExpectedManifestDigest);
