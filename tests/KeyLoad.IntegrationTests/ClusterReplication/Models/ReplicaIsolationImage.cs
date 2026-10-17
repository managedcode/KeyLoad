namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains safe actual OCI image identity and the inherited original rootfs chain.</summary>
internal sealed record ReplicaIsolationImage(string Id, string User, string Revision, string? FaultSource,
    string Os, string Architecture, string[] Layers);
