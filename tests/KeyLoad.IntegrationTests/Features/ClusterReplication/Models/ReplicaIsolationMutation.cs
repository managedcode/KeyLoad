namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Records mutation intent before invocation and the original observed native terminal result afterward.</summary>
internal sealed record ReplicaIsolationMutation(string ContainerId, string[] Arguments)
{
    public ContainerRuntimeProcessResult? Result { get; set; }
    public bool RemoteCompletionUnobserved { get; set; }
}
