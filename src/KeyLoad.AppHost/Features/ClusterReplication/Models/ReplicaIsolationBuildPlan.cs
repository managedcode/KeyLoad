namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Captures the native generated local tags before Aspire starts any fault-cohort build.</summary>
internal sealed record ReplicaIsolationBuildPlan(string BaseImage, string NativeBaseImage, string DockerfileSha256,
    string ContextPath, Guid Incarnation, IReadOnlyList<ReplicaIsolationBuildTarget> Targets);

/// <summary>Identifies one exact fixture resource and its native generated build tag.</summary>
internal sealed record ReplicaIsolationBuildTarget(string ResourceName, string ImageReference, string ServiceUser);
