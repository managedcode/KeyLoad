namespace KeyLoad.ClusterTests.QueryExecution;

[NotInParallel]
internal sealed class PostgresInt16ClusterWholeFlowTests
{
    [Test]
    public async Task Int16BindingRetainsNpgsqlSdkMcpExactPagesWidthOverflowRefusalRepairPolicyOriginalReceiptAndTwoColdCuts()
        => await PostgresInt16ClusterFlow.RunAsync();
}
