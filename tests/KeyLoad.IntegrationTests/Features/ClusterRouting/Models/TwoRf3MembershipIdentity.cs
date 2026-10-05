namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TwoRf3MembershipIdentity(Guid incarnation, string secret)
{
    internal Guid Incarnation { get; } = incarnation;
    internal string Secret { get; } = secret;
}
