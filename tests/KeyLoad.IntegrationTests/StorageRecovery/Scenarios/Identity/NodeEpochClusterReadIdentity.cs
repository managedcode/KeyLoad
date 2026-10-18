namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochClusterReadIdentity(string PrincipalId, string Secret, PrincipalRecord Principal,
    ApiKeyRecord Credential)
{
    public override string ToString() => "NodeEpochClusterReadIdentity(<private>)";
}
