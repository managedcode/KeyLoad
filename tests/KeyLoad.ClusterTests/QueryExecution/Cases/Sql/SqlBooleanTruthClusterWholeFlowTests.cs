namespace KeyLoad.ClusterTests.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlBooleanTruthClusterWholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task BooleanTruthPredicatesRetainNpgsqlSdkMcpWholePagesMissingUnknownTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBooleanTruthClusterFlow.RunAsync();
}
