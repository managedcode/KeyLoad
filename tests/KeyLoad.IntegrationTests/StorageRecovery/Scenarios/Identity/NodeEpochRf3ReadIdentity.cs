namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3ReadIdentity(string PrincipalId, string Secret, PrincipalRecord Principal,
    ApiKeyRecord Credential)
{
    public override string ToString() => "NodeEpochRf3ReadIdentity(<private>)";
}
