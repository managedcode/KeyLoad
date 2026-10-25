namespace KeyLoad.ClusterTests.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlNullDistinctClusterWholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task NullDistinctComparisonsRetainNpgsqlSdkMcpWholePagesMissingUnknownTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlNullDistinctClusterFlow.RunAsync();
}
