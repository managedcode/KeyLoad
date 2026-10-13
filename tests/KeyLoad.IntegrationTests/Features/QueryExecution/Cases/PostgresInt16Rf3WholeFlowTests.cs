namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class PostgresInt16Rf3WholeFlowTests
{
    [Test]
    public async Task Int16BindingRetainsNpgsqlSdkMcpExactPagesWidthOverflowRefusalRepairPolicyOriginalReceiptAndTwoColdCuts()
        => await PostgresInt16Rf3Flow.RunAsync();
}
