namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class PostgresDecimalRf3WholeFlowTests
{
    [Test]
    public async Task DecimalNumericBindingRetainsNpgsqlSdkMcpExactPagesRefusalRepairPolicyOriginalReceiptAndTwoColdCuts()
        => await PostgresDecimalRf3Flow.RunAsync();
}
