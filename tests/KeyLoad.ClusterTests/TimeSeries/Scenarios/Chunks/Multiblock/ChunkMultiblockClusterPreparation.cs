using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class ChunkMultiblockClusterPreparation
{
    internal static async Task<ChunkMultiblockClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var execution = await TimeSeriesClusterScenario.CreateAsync(fixture, token);
        var scenario = new SampleChunkClusterScenario(execution, Guid.NewGuid(), TimeSeriesClusterScenario.Start,
            TimeSeriesClusterScenario.Start.AddHours(SampleChunkClusterProtocol.WindowHours));
        var reader = await McpPersistedIdentity.CreateAsync(fixture, execution.Partition,
            TimeSeriesClusterScenario.Set, Capability.SeriesRead | Capability.Query, token);
        var state = new ChunkMultiblockClusterState(scenario, reader);
        await ChunkMultiblockClusterWrites.CommitAsync(administrator, state,
            new OpenSampleChunkWindow(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                scenario.WindowId, scenario.From, scenario.Until), "openSampleChunkWindow", 1, false, ChunkMultiblockClusterPhase.Opened, token);
        await ChunkMultiblockClusterWrites.CommitAsync(administrator, state,
            new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                ChunkMultiblockClusterData.Original, TimeSeriesClusterScenario.PublicTags), "appendSamples", ChunkMultiblockClusterData.OriginalCount, true, ChunkMultiblockClusterPhase.OriginalPending, token);
        await ChunkMultiblockClusterWrites.WaitAsync(administrator, state, healthy: false, token);
        return state;
    }
}
