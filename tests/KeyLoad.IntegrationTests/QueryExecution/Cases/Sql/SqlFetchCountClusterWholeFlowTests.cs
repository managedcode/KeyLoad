namespace KeyLoad.IntegrationTests.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlFetchCountClusterWholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task FetchCountRetainsNpgsqlSdkMcpAuthorityRefusalRepairReplayAndTwoColdCuts()
        => SqlFetchCountClusterFlow.RunAsync();
}
