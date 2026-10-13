namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlBooleanTruthRf3WholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task BooleanTruthPredicatesRetainNpgsqlSdkMcpWholePagesMissingUnknownTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBooleanTruthRf3Flow.RunAsync();
}
