namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class QueryEquivalentIntegerClusterTests
{
    [Test]
    public Task LosslessIntegralDecimalSqlJsonAndBuilderAgreeAcrossPublicCallersLivePolicyRepairAndBothColdCuts()
        => QueryEquivalentIntegerClusterFlow.RunAsync();
}
