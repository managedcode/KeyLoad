namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Captures the native generated local tags before Aspire starts any fault-cohort build.</summary>
internal sealed record ReplicaIsolationBuildPlan(string BaseImage, string NativeBaseImage, string DockerfileSha256,
    string ContextPath, Guid Incarnation, IReadOnlyList<ReplicaIsolationBuildTarget> Targets);
