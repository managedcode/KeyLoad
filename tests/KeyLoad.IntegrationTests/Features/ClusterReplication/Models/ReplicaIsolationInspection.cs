namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains the inspected native container/config/namespace generation without private environment data.</summary>
internal sealed record ReplicaIsolationInspection(string Id, string Name, string Image, string ConfigImage,
    string User, string StartedAt, string IpAddress, Guid Incarnation, string FaultSource);

/// <summary>Retains safe actual OCI image identity and the inherited original rootfs chain.</summary>
internal sealed record ReplicaIsolationImage(string Id, string User, string Revision, string? FaultSource,
    string Os, string Architecture, string[] Layers);

/// <summary>Binds actual signed native silo generation to its exact inspected owned namespace.</summary>
internal sealed record ReplicaIsolationNode(string Resource, ReplicaIsolationInspection Container, string SiloAddress);
