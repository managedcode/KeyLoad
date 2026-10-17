using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesExtremeAggregateFlow
{
    private const long InitialSequence = 5;
    private const long LateSequence = 6;
    private const long ExtremaSequence = 8;
    private const int PlacementVersion = 1;
    private const int ColdOwners = 2;
    private const int FirstIndex = 0;

    internal static async Task RunAsync(ClusterFixture fixture, ImmutableArray<SampleData> initial,
        ImmutableArray<SampleData> late, ImmutableArray<SampleData> extrema, SeriesExtremeAggregateRead read, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async administrator =>
            {
                var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
                var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
                    new(PlacementVersion, scenario.Partition), token));
                var current = new SeriesExtremeAggregateState(scenario, initial, late, extrema, placement);
                await SeriesExtremeAggregateSeed.OpenAsync(administrator, current, token);
                await SeriesExtremeAggregateSeed.AppendAsync(administrator, current, initial, InitialSequence, token);
                await SeriesExtremeAggregateSeed.AppendAsync(administrator, current, late, LateSequence, token);
                current.Reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
                    TimeSeriesRf3Scenario.Set, Capability.SeriesRead, token);
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, current.Reader.Secret,
                    reader => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, current.Reader.Secret,
                    async official =>
                    {
                        await SeriesExtremeAggregateSeed.CaptureAsync(administrator, current, token);
                        await SeriesExtremeAggregateWave.ReadAsync(administrator, reader, official, current, read, token);
                        await SeriesExtremeAggregateSeed.AppendAsync(administrator, current, extrema, ExtremaSequence, token);
                        current.HasExtrema = true;
                        await SeriesExtremeAggregateSeed.SealAsync(administrator, current, token);
                        current.Sealed = true;
                        await SeriesExtremeAggregateSeed.CaptureAsync(administrator, current, token);
                        await SeriesExtremeAggregateWave.ReadAsync(administrator, reader, official, current, read, token);
                        await SeriesExtremeAggregateWave.ReplayAsync(administrator, reader, current, token);
                        await SeriesExtremeAggregateSeed.AppendAsync(administrator, current, [extrema[FirstIndex]], ExtremaSequence, token);
                        await SeriesExtremeAggregateSeed.CaptureAsync(administrator, current, token);
                        await SeriesExtremeAggregateWave.ReadAsync(administrator, reader, official, current, read, token);
                        await SeriesExtremeAggregateWave.ReplayAsync(administrator, reader, current, token);
                        return current;
                    }, token), token);
            }, token);
        for (var cold = FirstIndex; cold < ColdOwners; cold++)
        {
            await SeriesUtcTiePublicCold.RunAsync(fixture, token);
            await VerifyAsync(fixture, state, read, token);
        }
    }

    private static Task<bool> VerifyAsync(ClusterFixture fixture, SeriesExtremeAggregateState state,
        SeriesExtremeAggregateRead read, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            administrator => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Reader!.Secret,
                reader => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret,
                async official =>
                {
                    await SeriesExtremeAggregateWave.ReadAsync(administrator, reader, official, state, read, token);
                    await SeriesExtremeAggregateWave.ReplayAsync(administrator, reader, state, token);
                    return true;
                }, token), token), token);
}
