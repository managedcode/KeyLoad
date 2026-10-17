using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-PQUERY-006 keeps the public operation available through surviving real RF3 members.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class PartitionQueryPublicLeaderLossTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcPquery006AcknowledgedSurvivorWriteIsVisibleToQueryAfterLeaderRejoins()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await PartitionQueryRf3Scenario.CreateAsync(fixture, deadline.Token).ConfigureAwait(false);
        await PartitionQueryRf3Leadership.RunAsync(fixture, scenario, deadline.Token).ConfigureAwait(false);
    }
}
