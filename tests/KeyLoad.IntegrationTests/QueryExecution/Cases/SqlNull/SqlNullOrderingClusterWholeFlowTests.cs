namespace KeyLoad.IntegrationTests.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlNullOrderingClusterWholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task ExplicitNullPlacementRetainsNpgsqlSdkMcpAuthorityRefusalRepairReplayAndTwoColdCuts()
        => SqlNullOrderingClusterFlow.RunAsync();
}
