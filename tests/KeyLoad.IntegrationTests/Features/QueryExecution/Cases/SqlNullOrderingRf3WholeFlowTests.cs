namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlNullOrderingRf3WholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task ExplicitNullPlacementRetainsNpgsqlSdkMcpAuthorityRefusalRepairReplayAndTwoColdCuts()
        => SqlNullOrderingRf3Flow.RunAsync();
}
