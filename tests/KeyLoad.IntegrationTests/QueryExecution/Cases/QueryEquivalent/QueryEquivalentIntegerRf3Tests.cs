namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class QueryEquivalentIntegerRf3Tests
{
    [Test]
    public Task LosslessIntegralDecimalSqlJsonAndBuilderAgreeAcrossPublicCallersLivePolicyRepairAndBothColdCuts()
        => QueryEquivalentIntegerRf3Flow.RunAsync();
}
