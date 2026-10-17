namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Original operation-owned native restore state; contains no raw secrets or request authority.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreSourceFile.SerializerAlias)]
internal sealed record ClusterRestoreSourceFile(
    [property: Orleans.Id(ClusterRestoreSourceFile.NameField)] string Name,
    [property: Orleans.Id(ClusterRestoreSourceFile.LengthField)] long Length,
    [property: Orleans.Id(ClusterRestoreSourceFile.ChecksumField)] string Checksum)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.source-file.v1";
    internal const int CurrentVersion = 1;
    private const int NameField = 0;
    private const int LengthField = 1;
    private const int ChecksumField = 2;
}
