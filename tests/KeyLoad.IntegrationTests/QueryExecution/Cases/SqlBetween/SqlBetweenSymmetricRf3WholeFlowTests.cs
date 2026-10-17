namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class SqlBetweenSymmetricRf3WholeFlowTests
{
    [global::TUnit.Core.TestAttribute]
    public Task BetweenSymmetricPredicatesRetainNpgsqlSdkMcpWholePagesMissingUnknownTypeRepairPolicyReplayAndTwoColdCuts()
        => SqlBetweenSymmetricRf3Flow.RunAsync();
}
