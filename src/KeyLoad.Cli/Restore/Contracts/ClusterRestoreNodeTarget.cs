namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Nonsecret separately configured node directory binding.</summary>
internal sealed record ClusterRestoreNodeTarget(Guid SourceOwnerId, string VoterId, string RelativeDataDirectory);
