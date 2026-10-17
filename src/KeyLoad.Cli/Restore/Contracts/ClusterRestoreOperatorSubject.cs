namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>One actual native captured operator identity; no credential bytes or trusted roles.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreOperatorSubject.SerializerAlias)]
internal sealed record ClusterRestoreOperatorSubject(
    [property: Orleans.Id(ClusterRestoreOperatorSubject.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreOperatorSubject.OwnerField)] Guid SourceOwnerId,
    [property: Orleans.Id(ClusterRestoreOperatorSubject.PrincipalField)] string PrincipalId,
    [property: Orleans.Id(ClusterRestoreOperatorSubject.EpochField)] long CapturedPolicyEpoch,
    [property: Orleans.Id(ClusterRestoreOperatorSubject.FingerprintField)] string CredentialFingerprint)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.operator-subject.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int OwnerField = 1;
    private const int PrincipalField = 2;
    private const int EpochField = 3;
    private const int FingerprintField = 4;
}
