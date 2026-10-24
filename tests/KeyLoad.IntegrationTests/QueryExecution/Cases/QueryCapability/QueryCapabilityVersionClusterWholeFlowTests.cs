namespace KeyLoad.IntegrationTests.QueryExecution;

[NotInParallel]
internal sealed class QueryCapabilityVersionClusterWholeFlowTests
{
    [Test]
    public Task ManifestVersionsAndBuilderSqlMcpPagesExplainPersistedReaderRepairAndTwoColdOwners()
        => QueryCapabilityVersionClusterFlow.RunAsync();
}
