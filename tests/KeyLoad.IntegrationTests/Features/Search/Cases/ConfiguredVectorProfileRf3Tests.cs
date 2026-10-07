using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ConfiguredVectorProfileRf3Tests(ClusterFixture fixture)
{
    [Test]
    [Arguments(ConfiguredVectorProfileRf3Flow.Sdk)]
    [Arguments(ConfiguredVectorProfileRf3Flow.Mcp)]
    [Arguments(ConfiguredVectorProfileRf3Flow.SdkSql)]
    [Arguments(ConfiguredVectorProfileRf3Flow.McpSql)]
    public async Task ConfiguredModelRejectsAtomicWriteOnEveryPublicPathAndCorrectRevisionSearchRemainsHealthy(string path)
    {
        using var deadline = McpCallerDeadline.Create();
        await ConfiguredVectorProfileRf3Flow.RunAsync(fixture, path, deadline.Token);
    }
}
