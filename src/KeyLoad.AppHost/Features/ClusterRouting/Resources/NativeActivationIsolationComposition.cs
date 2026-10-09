using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Diagnostic-only admission of the exact original six-owner resource cohort.</summary>
internal static class NativeActivationIsolationComposition
{
    private const string FirstNode = "node1";
    private const string SecondNode = "node2";
    private const string ThirdNode = "node3";
    private const string FourthNode = "node4";
    private const string FifthNode = "node5";
    private const string SixthNode = "node6";
    private static readonly string[] Names = [FirstNode, SecondNode, ThirdNode, FourthNode, FifthNode, SixthNode];
    internal static Task<ReplicaIsolationBuildPlan> ApplyAsync(IDistributedApplicationBuilder builder,
        string repository, string baseImage, Guid incarnation, CancellationToken cancellationToken)
        => ReplicaIsolationResourceComposition.ApplyAsync(builder, repository, baseImage, incarnation, Names, cancellationToken);
}
