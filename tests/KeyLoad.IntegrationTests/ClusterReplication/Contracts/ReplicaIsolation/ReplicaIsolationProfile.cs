namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Admits only the explicitly selected fixture-owned Linux namespace fault cohort.</summary>
internal enum ReplicaIsolationProfile
{
    OwnedLinuxNamespace = 1
}
