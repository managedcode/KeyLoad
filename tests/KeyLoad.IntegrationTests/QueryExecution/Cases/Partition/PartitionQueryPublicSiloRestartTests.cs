using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

/// <summary>AC-PQUERY-006 keeps the public operation available through surviving real Orleans members.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class PartitionQueryPublicSiloRestartTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcPquery006AcknowledgedSurvivorWriteIsVisibleToQueryAfterSiloRejoins()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await PartitionQueryClusterScenario.CreateAsync(fixture, deadline.Token).ConfigureAwait(false);
        await PartitionQueryClusterSiloRestart.RunAsync(fixture, scenario, deadline.Token).ConfigureAwait(false);
    }
}
