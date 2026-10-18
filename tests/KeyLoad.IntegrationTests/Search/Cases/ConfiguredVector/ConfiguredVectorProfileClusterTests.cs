using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ConfiguredVectorProfileClusterTests(ClusterFixture fixture)
{
    [Test]
    [Arguments(ConfiguredVectorProfileClusterFlow.Sdk)]
    [Arguments(ConfiguredVectorProfileClusterFlow.Mcp)]
    [Arguments(ConfiguredVectorProfileClusterFlow.SdkSql)]
    [Arguments(ConfiguredVectorProfileClusterFlow.McpSql)]
    public async Task ConfiguredModelRejectsAtomicWriteOnEveryPublicPathAndCorrectRevisionSearchRemainsHealthy(string path)
    {
        using var deadline = McpCallerDeadline.Create();
        await ConfiguredVectorProfileClusterFlow.RunAsync(fixture, path, deadline.Token);
    }
}
