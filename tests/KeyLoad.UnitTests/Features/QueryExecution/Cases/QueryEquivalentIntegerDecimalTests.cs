namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryEquivalentIntegerDecimalTests
{
    [Test]
    public Task LosslessIntegralDecimalBuildersRetainNativeSqlAstPagesAuthorityAndBothColdCuts()
        => QueryEquivalentIntegerDecimalFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
