using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Authorization;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class SafeProjectorOperationClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task SafeSourceAliasesRoundtripAndCurrentCdcSdkOfficialMcpRemainExactAcrossTwoColdRestarts()
    {
        await SafeProjectorOperationClusterTrial.RunAsync(fixture);
    }

    [Test]
    public Task SchemaVisibilityAndRawAuthorityRemainIndependentThroughSdkOfficialMcpAndBothColdCuts()
        => SchemaVisibleProjectionClusterFlow.RunAsync(fixture);
}
