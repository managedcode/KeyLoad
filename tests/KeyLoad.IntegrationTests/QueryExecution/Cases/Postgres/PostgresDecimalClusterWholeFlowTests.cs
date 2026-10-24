namespace KeyLoad.IntegrationTests.QueryExecution;

[NotInParallel]
internal sealed class PostgresDecimalClusterWholeFlowTests
{
    [Test]
    public async Task DecimalNumericBindingRetainsNpgsqlSdkMcpExactPagesRefusalRepairPolicyOriginalReceiptAndTwoColdCuts()
        => await PostgresDecimalClusterFlow.RunAsync();
}
