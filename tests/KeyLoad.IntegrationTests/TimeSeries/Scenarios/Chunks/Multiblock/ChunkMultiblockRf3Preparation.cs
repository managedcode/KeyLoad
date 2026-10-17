using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Preparation
{
    internal static async Task<ChunkMultiblockRf3State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var native = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
        var scenario = new SampleChunkRf3Scenario(native, Guid.NewGuid(), TimeSeriesRf3Scenario.Start,
            TimeSeriesRf3Scenario.Start.AddHours(SampleChunkRf3Protocol.WindowHours));
        var reader = await McpPersistedIdentity.CreateAsync(fixture, native.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead | Capability.Query, token);
        var state = new ChunkMultiblockRf3State(scenario, reader);
        await ChunkMultiblockRf3Writes.CommitAsync(administrator, state,
            new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                scenario.WindowId, scenario.From, scenario.Until), "openSampleChunkWindow", 1, false, ChunkMultiblockRf3Phase.Opened, token);
        await ChunkMultiblockRf3Writes.CommitAsync(administrator, state,
            new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                ChunkMultiblockRf3Data.Original, TimeSeriesRf3Scenario.PublicTags), "appendSamples", ChunkMultiblockRf3Data.OriginalCount, true, ChunkMultiblockRf3Phase.OriginalPending, token);
        await ChunkMultiblockRf3Writes.WaitAsync(administrator, state, healthy: false, token);
        return state;
    }
}
