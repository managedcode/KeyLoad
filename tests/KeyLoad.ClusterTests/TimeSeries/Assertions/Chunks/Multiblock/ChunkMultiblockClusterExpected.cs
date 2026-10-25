using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class ChunkMultiblockClusterExpected
{
    internal const long OriginalRevision = 260;
    internal const long HealthyRevision = 262;
    internal const long OriginalGeneration = 2;
    internal const long HealthyGeneration = 3;
    private const long FloorTick = 1;
    private const long OpenRevision = 1;
    private const long NoGeneration = 0;
    private const long NoRecords = 0;
    private const long PendingOriginalGeneration = 1;
    private const long PendingOriginalRevision = 259;
    private const long PendingHealthyGeneration = 2;
    private const long PendingHealthyRevision = 261;

    internal static ImmutableArray<SampleRecord> Visible(bool expired, bool healthy, bool opened = false)
        => opened ? [] : [.. ChunkMultiblockClusterData.Expected(healthy).Where(row => !expired ||
            row.Sample.Timestamp.UtcTicks >= TimeSeriesClusterScenario.Start.AddTicks(FloorTick).UtcTicks)];

    internal static SampleChunkWindowResult Window(ChunkMultiblockClusterState state, long generation, long opaqueCut)
    {
        var expectedGeneration = state.Opened ? NoGeneration : state.Healthy ? HealthyGeneration : OriginalGeneration;
        var expectedRevision = state.Opened ? OpenRevision : state.Healthy ? HealthyRevision : OriginalRevision;
        if (state.Phase == ChunkMultiblockClusterPhase.OriginalPending && generation == PendingOriginalGeneration)
        {
            expectedGeneration = PendingOriginalGeneration;
            expectedRevision = PendingOriginalRevision;
        }
        if (state.Phase == ChunkMultiblockClusterPhase.HealthyPending && generation == PendingHealthyGeneration)
        {
            expectedGeneration = PendingHealthyGeneration;
            expectedRevision = PendingHealthyRevision;
        }
        return new(state.Scenario.WindowId, state.Scenario.From, state.Scenario.Until, expectedGeneration, expectedRevision,
            state.Opened ? NoRecords : state.Healthy ? ChunkMultiblockClusterData.HealthyCount : ChunkMultiblockClusterData.OriginalCount,
            state.Expired ? state.Scenario.From.AddTicks(FloorTick).UtcTicks : null,
            Visible(state.Expired, state.Healthy, state.Opened), opaqueCut);
    }

    internal static SampleAggregate Aggregate(bool expired, bool healthy, bool opened = false)
        => opened ? new(0, 0, null, null, null) : expired ? healthy ? new(2, 8, -0d, 8, 4) : new(1, 0, -0d, -0d, 0)
            : healthy ? new(258, 522, -0d, 8, 522d / 258) : new(257, 514, -0d, 4, 2);
    internal static SampleAggregateWindowsResult Windows(bool expired, bool healthy, bool opened = false)
    {
        var at = TimeSeriesClusterScenario.Start;
        return new([new(at, at.AddTicks(1), expired || opened ? new(0, 0, null, null, null) : new(256, 514, 2, 4, 514d / 256)),
            new(at.AddTicks(1), at.AddTicks(2), opened ? new(0, 0, null, null, null) : new(1, 0, -0d, -0d, 0)),
            new(at.AddTicks(2), at.AddTicks(3), healthy ? new(1, 8, 8, 8, 8) : new(0, 0, null, null, null))]);
    }
}
