namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class QueryEquivalentIntegerDecimalTests
{
    [Test]
    public Task LosslessIntegralDecimalBuildersRetainSqlAstPagesAuthorityAndBothColdCuts()
        => QueryEquivalentIntegerDecimalFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
