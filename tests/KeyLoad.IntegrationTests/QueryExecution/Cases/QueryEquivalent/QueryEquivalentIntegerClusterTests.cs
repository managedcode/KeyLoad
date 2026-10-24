namespace KeyLoad.IntegrationTests.QueryExecution;

[NotInParallel]
internal sealed class QueryEquivalentIntegerClusterTests
{
    [Test]
    public Task LosslessIntegralDecimalSqlJsonAndBuilderAgreeAcrossPublicCallersLivePolicyRepairAndBothColdCuts()
        => QueryEquivalentIntegerClusterFlow.RunAsync();
}
