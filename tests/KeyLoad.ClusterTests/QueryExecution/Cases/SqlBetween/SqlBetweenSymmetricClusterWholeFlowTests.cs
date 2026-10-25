namespace KeyLoad.ClusterTests.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlBetweenSymmetricClusterWholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task BetweenSymmetricPredicatesRetainNpgsqlSdkMcpWholePagesMissingUnknownTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBetweenSymmetricClusterFlow.RunAsync();
}
