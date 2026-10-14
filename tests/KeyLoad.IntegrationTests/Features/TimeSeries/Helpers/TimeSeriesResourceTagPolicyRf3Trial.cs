using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesResourceTagPolicyRf3Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var created = await TimeSeriesResourceTagPolicyRf3Seed.CreateAsync(fixture, administrator, token);
                await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    created.Reader.Secret, async reader =>
                    {
                        await DiscoverAsync(reader, token);
                        await TimeSeriesResourceTagPolicyRf3Policy.WitnessAsync(administrator, created, token);
                        await TimeSeriesResourceTagPolicyRf3Assertions.ReadAsync(reader, administrator, created, token);
                        await TimeSeriesResourceTagPolicyRf3Policy.ChangeAsync(administrator, created, TimeSeriesResourceTagPolicyRf3Seed.ChangedGrant, token);
                        await TimeSeriesResourceTagPolicyRf3Assertions.ReadAsync(reader, administrator, created, token);
                        await TimeSeriesResourceTagPolicyRf3Policy.StaleAsync(reader, administrator, created, token);
                        await TimeSeriesResourceTagPolicyRf3Policy.ReplayAsync(reader, administrator, created, token);
                        return true;
                    }, token);
                return created;
            }, token);
        await TimeSeriesResourceTagPolicyRf3Restart.RunAsync(fixture, token);
        await ObserveAsync(fixture, state, repair: true, token);
        await TimeSeriesResourceTagPolicyRf3Restart.RunAsync(fixture, token);
        await ObserveAsync(fixture, state, repair: false, token);
    }

    private static Task<bool> ObserveAsync(ClusterFixture fixture, TimeSeriesResourceTagPolicyRf3State state,
        bool repair, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey,
            administrator => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Reader.Secret, async reader =>
                {
                    await administrator.AssertFreshRoutingReadyAsync(token);
                    await reader.AssertFreshRoutingReadyAsync(token);
                    await DiscoverAsync(reader, token);
                    await TimeSeriesResourceTagPolicyRf3Policy.WitnessAsync(administrator, state, token);
                    await TimeSeriesResourceTagPolicyRf3Assertions.ReadAsync(reader, administrator, state, token);
                    await TimeSeriesResourceTagPolicyRf3Policy.StaleAsync(reader, administrator, state, token);
                    await TimeSeriesResourceTagPolicyRf3Policy.ReplayAsync(reader, administrator, state, token);
                    if (repair)
                    {
                        await TimeSeriesResourceTagPolicyRf3Policy.ChangeAsync(administrator, state, TimeSeriesRf3Scenario.SecretGrant, token);
                        await TimeSeriesResourceTagPolicyRf3Assertions.ReadAsync(reader, administrator, state, token);
                        await TimeSeriesResourceTagPolicyRf3Policy.StaleAsync(reader, administrator, state, token);
                        await TimeSeriesResourceTagPolicyRf3Policy.ReplayAsync(reader, administrator, state, token);
                    }
                    return true;
                }, token), token);

    private static async Task DiscoverAsync(RequestCqrsRf3Callers reader, CancellationToken token)
    {
        foreach (var tool in new[] { McpCallerTools.SeriesRead, McpCallerTools.SeriesLatest, McpCallerTools.SeriesAggregate })
        {
            await McpDiscoveryAssertions.VerifyAsync(await reader.Mcp.Client.DiscoverKeyLoadToolAsync(tool, token));
        }
    }
}
