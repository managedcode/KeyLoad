using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
namespace KeyLoad.ClusterTests.TimeSeries;

internal static class TimeSeriesResourceTagPolicyClusterTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var created = await TimeSeriesResourceTagPolicyClusterSeed.CreateAsync(fixture, administrator, token);
                await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    created.Reader.Secret, async reader =>
                    {
                        await DiscoverAsync(reader, token);
                        await TimeSeriesResourceTagPolicyClusterPolicy.WitnessAsync(administrator, created, token);
                        await TimeSeriesResourceTagPolicyClusterAssertions.ReadAsync(reader, administrator, created, token);
                        await TimeSeriesResourceTagPolicyClusterPolicy.ChangeAsync(administrator, created, TimeSeriesResourceTagPolicyClusterSeed.ChangedGrant, token);
                        await TimeSeriesResourceTagPolicyClusterAssertions.ReadAsync(reader, administrator, created, token);
                        await TimeSeriesResourceTagPolicyClusterPolicy.StaleAsync(reader, administrator, created, token);
                        await TimeSeriesResourceTagPolicyClusterPolicy.ReplayAsync(reader, administrator, created, token);
                        return true;
                    }, token);
                return created;
            }, token);
        await TimeSeriesResourceTagPolicyClusterRestart.RunAsync(fixture, token);
        await ObserveAsync(fixture, state, repair: true, token);
        await TimeSeriesResourceTagPolicyClusterRestart.RunAsync(fixture, token);
        await ObserveAsync(fixture, state, repair: false, token);
    }

    private static Task<bool> ObserveAsync(ClusterFixture fixture, TimeSeriesResourceTagPolicyClusterState state,
        bool repair, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey,
            administrator => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Reader.Secret, async reader =>
                {
                    await administrator.AssertFreshRoutingReadyAsync(token);
                    await reader.AssertFreshRoutingReadyAsync(token);
                    await DiscoverAsync(reader, token);
                    await TimeSeriesResourceTagPolicyClusterPolicy.WitnessAsync(administrator, state, token);
                    await TimeSeriesResourceTagPolicyClusterAssertions.ReadAsync(reader, administrator, state, token);
                    await TimeSeriesResourceTagPolicyClusterPolicy.StaleAsync(reader, administrator, state, token);
                    await TimeSeriesResourceTagPolicyClusterPolicy.ReplayAsync(reader, administrator, state, token);
                    if (repair)
                    {
                        await TimeSeriesResourceTagPolicyClusterPolicy.ChangeAsync(administrator, state, TimeSeriesClusterScenario.SecretGrant, token);
                        await TimeSeriesResourceTagPolicyClusterAssertions.ReadAsync(reader, administrator, state, token);
                        await TimeSeriesResourceTagPolicyClusterPolicy.StaleAsync(reader, administrator, state, token);
                        await TimeSeriesResourceTagPolicyClusterPolicy.ReplayAsync(reader, administrator, state, token);
                    }
                    return true;
                }, token), token);

    private static async Task DiscoverAsync(RequestCqrsClusterCallers reader, CancellationToken token)
    {
        foreach (var tool in new[] { McpCallerTools.SeriesRead, McpCallerTools.SeriesLatest, McpCallerTools.SeriesAggregate, SampleChunkProtocol.ReadTool })
        {
            await McpDiscoveryAssertions.VerifyAsync(await reader.Mcp.Client.DiscoverKeyLoadToolAsync(tool, token));
        }
    }
}
