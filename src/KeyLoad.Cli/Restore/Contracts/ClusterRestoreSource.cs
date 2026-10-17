using System.Collections.Immutable;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Original operation-owned native restore state; contains no raw secrets or request authority.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreSource.SerializerAlias)]
internal sealed record ClusterRestoreSource(
    [property: Orleans.Id(ClusterRestoreSource.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreSource.CanonicalPathField)] string CanonicalPath,
    [property: Orleans.Id(ClusterRestoreSource.OriginalCutField)] ClusterBackupOwnerCut OriginalCut,
    [property: Orleans.Id(ClusterRestoreSource.FilesField)] ImmutableArray<ClusterRestoreSourceFile> Files,
    [property: Orleans.Id(ClusterRestoreSource.EnvelopeDigestField)] string EnvelopeDigest)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.source.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int CanonicalPathField = 1;
    private const int OriginalCutField = 2;
    private const int FilesField = 3;
    private const int EnvelopeDigestField = 4;
}
