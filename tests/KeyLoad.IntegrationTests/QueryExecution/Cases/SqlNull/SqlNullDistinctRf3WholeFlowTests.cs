namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlNullDistinctRf3WholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task NullDistinctComparisonsRetainNpgsqlSdkMcpWholePagesMissingUnknownTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlNullDistinctRf3Flow.RunAsync();
}
