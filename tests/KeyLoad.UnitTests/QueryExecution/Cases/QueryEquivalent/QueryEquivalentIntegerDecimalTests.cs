namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class QueryEquivalentIntegerDecimalTests
{
    [Test]
    public Task LosslessIntegralDecimalBuildersRetainSqlAstPagesAuthorityAndBothColdCuts()
        => QueryEquivalentIntegerDecimalFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
