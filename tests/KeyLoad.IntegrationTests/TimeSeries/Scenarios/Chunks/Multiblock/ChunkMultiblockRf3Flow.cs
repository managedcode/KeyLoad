using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Flow
{
    private const int DeletePage = 128;
    private const long PurgedCount = 256;
    private const int ColdOwners = 2;

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var prepared = await ChunkMultiblockRf3Preparation.CreateAsync(fixture, administrator, token);
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3,
                    prepared.Reader.Secret, async reader =>
                    {
                        await ChunkMultiblockRf3Current.RequireAsync(administrator, reader, prepared, token);
                        await ChunkMultiblockRf3Policy.RunAsync(administrator, reader, prepared, token);
                        await ExpireAsync(administrator, reader, prepared, first: true, token);
                        await ExpireAsync(administrator, reader, prepared, first: false, token);
                        await ChunkMultiblockRf3Refusal.RunAsync(administrator, reader, prepared, token);
                        await ChunkMultiblockRf3Writes.CommitAsync(administrator, prepared,
                            new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                                [ChunkMultiblockRf3Data.Fresh], TimeSeriesRf3Scenario.PublicTags), "appendSamples", ChunkMultiblockRf3Data.HealthyCount, false, ChunkMultiblockRf3Phase.HealthyPending, token);
                        await ChunkMultiblockRf3Writes.WaitAsync(administrator, prepared, healthy: true, token);
                        await ChunkMultiblockRf3Current.RequireAsync(administrator, reader, prepared, token);
                        await ChunkMultiblockRf3Writes.ReplayAsync(administrator, prepared, token);
                        return prepared;
                    }, token);
            }, token);
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            await CredentialAuthorityCold.RestartAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, administrator => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App,
                    McpCallerProtocol.Node3, state.Reader.Secret, async reader =>
                    {
                        await ChunkMultiblockRf3Current.RequireAsync(administrator, reader, state, token);
                        await ChunkMultiblockRf3Refusal.RunAsync(administrator, reader, state, token);
                        return true;
                    }, token), token);
        }
    }

    private static async Task ExpireAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        ChunkMultiblockRf3State state, bool first, CancellationToken token)
    {
        await ChunkMultiblockRf3Writes.CommitAsync(administrator, state,
            new ExpireSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                state.Scenario.From.AddTicks(1), DeletePage), "expireSamples", first ? DeletePage : PurgedCount, !first,
            first ? ChunkMultiblockRf3Phase.FirstExpired : ChunkMultiblockRf3Phase.SecondExpired, token);
        await ChunkMultiblockRf3Current.RequireAsync(administrator, reader, state, token);
        await ChunkMultiblockRf3Writes.ReplayAsync(administrator, state, token);
    }
}
