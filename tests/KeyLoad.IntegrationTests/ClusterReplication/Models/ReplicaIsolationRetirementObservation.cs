namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains an actual owned retirement process outcome or a successfully acquired exclusive lock.</summary>
internal sealed record ReplicaIsolationRetirementObservation(string Operation, string Target,
    ContainerRuntimeProcessResult? Process, bool ExclusiveLockAcquired);
