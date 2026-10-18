using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockClusterFlow
{
    private const int DeletePage = 128;
    private const long PurgedCount = 256;
    private const int ColdOwners = 2;

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var prepared = await ChunkMultiblockClusterPreparation.CreateAsync(fixture, administrator, token);
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3,
                    prepared.Reader.Secret, async reader =>
                    {
                        await ChunkMultiblockClusterCurrent.RequireAsync(administrator, reader, prepared, token);
                        await ChunkMultiblockClusterPolicy.RunAsync(administrator, reader, prepared, token);
                        await ExpireAsync(administrator, reader, prepared, first: true, token);
                        await ExpireAsync(administrator, reader, prepared, first: false, token);
                        await ChunkMultiblockClusterRefusal.RunAsync(administrator, reader, prepared, token);
                        await ChunkMultiblockClusterWrites.CommitAsync(administrator, prepared,
                            new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                                [ChunkMultiblockClusterData.Fresh], TimeSeriesClusterScenario.PublicTags), "appendSamples", ChunkMultiblockClusterData.HealthyCount, false, ChunkMultiblockClusterPhase.HealthyPending, token);
                        await ChunkMultiblockClusterWrites.WaitAsync(administrator, prepared, healthy: true, token);
                        await ChunkMultiblockClusterCurrent.RequireAsync(administrator, reader, prepared, token);
                        await ChunkMultiblockClusterWrites.ReplayAsync(administrator, prepared, token);
                        return prepared;
                    }, token);
            }, token);
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            await CredentialAuthorityCold.RestartAsync(fixture, token);
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, administrator => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App,
                    McpCallerProtocol.Node3, state.Reader.Secret, async reader =>
                    {
                        await ChunkMultiblockClusterCurrent.RequireAsync(administrator, reader, state, token);
                        await ChunkMultiblockClusterRefusal.RunAsync(administrator, reader, state, token);
                        return true;
                    }, token), token);
        }
    }

    private static async Task ExpireAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        ChunkMultiblockClusterState state, bool first, CancellationToken token)
    {
        await ChunkMultiblockClusterWrites.CommitAsync(administrator, state,
            new ExpireSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                state.Scenario.From.AddTicks(1), DeletePage), "expireSamples", first ? DeletePage : PurgedCount, !first,
            first ? ChunkMultiblockClusterPhase.FirstExpired : ChunkMultiblockClusterPhase.SecondExpired, token);
        await ChunkMultiblockClusterCurrent.RequireAsync(administrator, reader, state, token);
        await ChunkMultiblockClusterWrites.ReplayAsync(administrator, state, token);
    }
}
