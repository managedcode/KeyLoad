namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Preserves exact-two-peer admission for the unchanged fixed-three flow.</summary>
internal sealed class ReplicaIsolationRules : IReplicaIsolationRulePlan
{
    private readonly ReplicaIsolationRulePlan plan;
    internal ReplicaIsolationRules(Guid operation, IReadOnlyList<string> peers)
        => plan = new(operation, peers, ReplicaIsolationProtocol.PeerCount);
    public string InputChain => plan.InputChain;
    public string OutputChain => plan.OutputChain;
    public IEnumerable<string[]> Installation() => plan.Installation();
    internal static string[] NativeArguments(string container, IReadOnlyList<string> rule)
        => ReplicaIsolationRulePlan.NativeArguments(container, rule);
    internal static string RequireIpv4(string value) => ReplicaIsolationRulePlan.RequireIpv4(value);
}
