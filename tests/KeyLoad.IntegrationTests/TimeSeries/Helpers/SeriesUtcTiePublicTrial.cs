using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesUtcTiePublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, ImmutableArray<SampleData> original, CancellationToken token)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, TimeSeriesRf3Scenario.Set, Capability.SeriesRead, token);
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async administrator =>
            {
                var request = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
                var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
                await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                    await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
                var current = new SeriesUtcTiePublicState(scenario, original, identity, placement);
                await SeriesUtcTiePublicSeed.CommitAsync(administrator, current, SeriesUtcTiePublicSeed.Original(current), 4L, false, token);
                current.Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, identity.Secret,
                    async reader =>
                    {
                        await SeriesUtcTiePublicAssertions.OriginalAsync(administrator, reader, current, token);
                        await SeriesUtcTiePublicAssertions.RequireAsync(administrator, reader, current, token);
                        await SeriesUtcTiePublicObservation.ReplayAsync(administrator, reader, current, token);
                        await SeriesUtcTiePublicSeed.CommitAsync(administrator, current, SeriesUtcTiePublicSeed.Expire(current), 1L, true, token);
                        current.Purged = true;
                        current.Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
                        await SeriesUtcTiePublicAssertions.RequireAsync(administrator, reader, current, token);
                        await SeriesUtcTiePublicObservation.ReplayAsync(administrator, reader, current, token);
                        return current;
                    }, token);
            }, token);
        await SeriesUtcTiePublicCold.RunAsync(fixture, token);
        await ContinueAsync(fixture, state, token);
        await SeriesUtcTiePublicCold.RunAsync(fixture, token);
        await VerifyAsync(fixture, state, token);
    }

    private static Task<bool> ContinueAsync(ClusterFixture fixture, SeriesUtcTiePublicState state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            administrator => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Reader.Secret,
                async reader =>
                {
                    await SeriesUtcTiePublicAssertions.RequireAsync(administrator, reader, state, token);
                    await SeriesUtcTiePublicObservation.ReplayAsync(administrator, reader, state, token);
                    await SeriesUtcTiePublicSeed.CommitAsync(administrator, state, SeriesUtcTiePublicSeed.Deduplicate(state), 4L, false, token);
                    state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
                    await SeriesUtcTiePublicAssertions.RequireAsync(administrator, reader, state, token);
                    await SeriesUtcTiePublicObservation.ReplayAsync(administrator, reader, state, token);
                    await SeriesUtcTiePublicSeed.CommitAsync(administrator, state, SeriesUtcTiePublicSeed.Fresh(state), 5L, false, token);
                    state.Fresh = true;
                    state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
                    await SeriesUtcTiePublicAssertions.RequireAsync(administrator, reader, state, token);
                    await SeriesUtcTiePublicObservation.ReplayAsync(administrator, reader, state, token);
                    return true;
                }, token), token);

    private static Task<bool> VerifyAsync(ClusterFixture fixture, SeriesUtcTiePublicState state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            administrator => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Reader.Secret,
                async reader =>
                {
                    await SeriesUtcTiePublicAssertions.RequireAsync(administrator, reader, state, token);
                    await SeriesUtcTiePublicObservation.ReplayAsync(administrator, reader, state, token);
                    return true;
                }, token), token);
}
