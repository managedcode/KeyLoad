namespace KeyLoad.Core;

[Orleans.GenerateSerializer, Orleans.Alias(ClusterBackupOwnerCapability.SerializerAlias)]
internal sealed record ClusterBackupOwnerCapability(
    [property: Orleans.Id(ClusterBackupOwnerCapability.RequestField)] ClusterBackupOwnerRequest Request,
    [property: Orleans.Id(ClusterBackupOwnerCapability.CredentialField)] DatabaseCredentialWitness Credential)
{
    internal const string SerializerAlias = "keyload.core.cluster-backup-owner-capability.v1";
    private const int RequestField = 0;
    private const int CredentialField = 1;
}
