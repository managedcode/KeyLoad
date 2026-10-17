namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Binds actual signed native silo generation to its exact inspected owned namespace.</summary>
internal sealed record ReplicaIsolationNode(string Resource, ReplicaIsolationInspection Container, string SiloAddress);
