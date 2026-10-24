namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class QueryCapabilityVersionWholeFlowTests
{
    [Test]
    public Task ManifestVersionsRoundTripBuilderAndSqlPagesExplainCurrentReaderAndTwoColdOwners()
        => QueryCapabilityVersionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
