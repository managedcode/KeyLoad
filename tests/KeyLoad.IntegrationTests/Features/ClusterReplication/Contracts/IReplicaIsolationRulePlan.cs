namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>The immutable admitted chain/rule shape shared by distinct closed topology owners.</summary>
internal interface IReplicaIsolationRulePlan
{
    string InputChain { get; }
    string OutputChain { get; }
    IEnumerable<string[]> Installation();
}
