namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class QueryCapabilityParityWholeFlowTests
{
    [Test]
    public Task ManifestRoundTripsBuilderAndSqlPagesExplainCurrentReaderAndTwoColdOwners()
        => QueryCapabilityParityFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
