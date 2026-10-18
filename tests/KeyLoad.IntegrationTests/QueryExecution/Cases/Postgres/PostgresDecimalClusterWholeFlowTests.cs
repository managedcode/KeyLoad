namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class PostgresDecimalClusterWholeFlowTests
{
    [Test]
    public async Task DecimalNumericBindingRetainsNpgsqlSdkMcpExactPagesRefusalRepairPolicyOriginalReceiptAndTwoColdCuts()
        => await PostgresDecimalClusterFlow.RunAsync();
}
