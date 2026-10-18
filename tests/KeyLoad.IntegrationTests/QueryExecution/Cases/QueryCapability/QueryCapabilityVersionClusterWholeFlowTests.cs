namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class QueryCapabilityVersionClusterWholeFlowTests
{
    [Test]
    public Task ManifestVersionsAndBuilderSqlMcpPagesExplainPersistedReaderRepairAndTwoColdOwners()
        => QueryCapabilityVersionClusterFlow.RunAsync();
}
