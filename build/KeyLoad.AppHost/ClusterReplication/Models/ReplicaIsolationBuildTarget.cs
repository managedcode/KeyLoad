namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Identifies one exact fixture resource and its native generated build tag.</summary>
internal sealed record ReplicaIsolationBuildTarget(string ResourceName, string ImageReference, string ServiceUser);
