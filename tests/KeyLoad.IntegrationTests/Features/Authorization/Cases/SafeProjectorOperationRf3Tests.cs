using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class SafeProjectorOperationRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task SafeSourceAliasesRoundtripAndCurrentCdcSdkOfficialMcpRemainExactAcrossTwoColdRestarts()
    {
        await SafeProjectorOperationRf3Trial.RunAsync(fixture);
    }

    [Test]
    public Task SchemaVisibilityAndRawAuthorityRemainIndependentThroughSdkOfficialMcpAndBothColdCuts()
        => SchemaVisibleProjectionRf3Flow.RunAsync(fixture);
}
