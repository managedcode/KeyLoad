namespace KeyLoad.ClusterTests.QueryExecution;

[NotInParallel]
internal sealed class QueryCapabilityParityClusterWholeFlowTests
{
    [Test]
    public Task ManifestAndBuilderSqlMcpPagesExplainPersistedReaderRepairAndTwoColdOwners()
        => QueryCapabilityParityClusterFlow.RunAsync();
}
