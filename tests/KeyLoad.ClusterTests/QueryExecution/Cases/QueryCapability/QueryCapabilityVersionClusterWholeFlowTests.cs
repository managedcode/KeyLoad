namespace KeyLoad.ClusterTests.QueryExecution;

[NotInParallel]
internal sealed class QueryCapabilityVersionClusterWholeFlowTests
{
    [Test]
    public Task ManifestVersionsAndBuilderSqlMcpPagesExplainPersistedReaderRepairAndTwoColdOwners()
        => QueryCapabilityVersionClusterFlow.RunAsync();
}
