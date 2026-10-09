namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Admits only the unchanged original fixed-three fault topology.</summary>
internal static class ReplicaIsolationComposition
{
    internal const string Stage = "fault-runtime";
    internal const string Dockerfile = "src/KeyLoad.AppHost/Features/ClusterReplication/Containers/FaultIsolation/Dockerfile";
    internal const string BaseArgument = "KEYLOAD_SERVER_IMAGE";
    internal const string SourceArgument = "KEYLOAD_FAULT_SOURCE";
    internal const string Capability = "--cap-add=NET_ADMIN";
    internal const string IncarnationLabel = "keyload.fault.incarnation";
    private const string FirstNode = "node1";
    private const string SecondNode = "node2";
    private const string ThirdNode = "node3";
    private static readonly string[] Names = [FirstNode, SecondNode, ThirdNode];
    internal static Task<ReplicaIsolationBuildPlan> ApplyAsync(IDistributedApplicationBuilder builder,
        string repository, string baseImage, Guid incarnation, CancellationToken cancellationToken)
        => ReplicaIsolationResourceComposition.ApplyAsync(builder, repository, baseImage, incarnation, Names, cancellationToken);
}
