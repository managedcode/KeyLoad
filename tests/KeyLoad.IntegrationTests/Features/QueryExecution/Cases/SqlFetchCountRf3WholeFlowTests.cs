namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlFetchCountRf3WholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task FetchCountRetainsNpgsqlSdkMcpAuthorityRefusalRepairReplayAndTwoColdCuts()
        => SqlFetchCountRf3Flow.RunAsync();
}
