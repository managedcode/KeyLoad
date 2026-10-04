namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record DueNoQuorumRf3Creator(PrincipalRecord Principal, string Secret)
{
    public override string ToString() => "DueNoQuorumRf3Creator(<private>)";
}
