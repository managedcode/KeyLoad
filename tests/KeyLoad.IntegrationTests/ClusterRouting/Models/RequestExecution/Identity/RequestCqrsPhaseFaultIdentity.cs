namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Contains the actual persisted non-administrator identity and one owned document scenario.</summary>
internal sealed record RequestCqrsPhaseFaultIdentity(PartitionRef Partition, string PrincipalId,
    string Secret)
{
    public override string ToString() => "RequestCqrsPhaseFaultIdentity(<private>)";
}
