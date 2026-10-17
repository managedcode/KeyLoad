using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Isolates one actual native silo from all five other owned peers.</summary>
internal sealed class NativeActivationIsolationRules : IReplicaIsolationRulePlan
{
    private const int PeerCount = 5;
    private readonly ReplicaIsolationRulePlan plan;
    internal NativeActivationIsolationRules(Guid operation, IReadOnlyList<string> peers)
        => plan = new(operation, peers, PeerCount);
    public string InputChain => plan.InputChain;
    public string OutputChain => plan.OutputChain;
    public IEnumerable<string[]> Installation() => plan.Installation();
}
