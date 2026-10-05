using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsAuthorityFaultIdentity(
    PartitionRef Partition,
    PrincipalRecord Principal,
    ApiKeyRecord Credential,
    string Secret)
{
    public override string ToString() => "RequestCqrsAuthorityFaultIdentity";
}
