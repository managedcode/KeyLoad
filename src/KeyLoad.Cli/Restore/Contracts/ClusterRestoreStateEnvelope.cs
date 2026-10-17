namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Original operation-owned native restore state; contains no raw secrets or request authority.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreStateEnvelope.SerializerAlias)]
internal sealed record ClusterRestoreStateEnvelope(
    [property: Orleans.Id(ClusterRestoreStateEnvelope.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreStateEnvelope.PayloadField)] ReadOnlyMemory<byte> Payload,
    [property: Orleans.Id(ClusterRestoreStateEnvelope.ChecksumField)] ReadOnlyMemory<byte> Checksum)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.state-envelope.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int PayloadField = 1;
    private const int ChecksumField = 2;
}
