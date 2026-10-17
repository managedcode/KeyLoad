namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class QueryCapabilityVersionRf3WholeFlowTests
{
    [Test]
    public Task ManifestVersionsAndBuilderSqlMcpPagesExplainPersistedReaderRepairAndTwoColdOwners()
        => QueryCapabilityVersionRf3Flow.RunAsync();
}
