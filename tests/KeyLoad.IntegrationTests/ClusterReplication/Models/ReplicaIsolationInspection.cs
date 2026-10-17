namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains the inspected native container/config/namespace generation without private environment data.</summary>
internal sealed record ReplicaIsolationInspection(string Id, string Name, string Image, string ConfigImage,
    string User, string StartedAt, string IpAddress, Guid Incarnation, string FaultSource);
