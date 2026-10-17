using System.Globalization;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnReciprocalObservation
{
    private const string Prefix = "ANN_RECIPROCAL_OBSERVATION v=1";

    internal static (PackedAnnState State, long ElapsedTicks, long AllocatedBytes, long TimestampFrequency) Build(
        VectorSpace space, VectorRecord[] records, PackedAnnOptions options, AnnWorkBudget budget)
    {
        var allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        var state = PackedAnnBuilder.Build(space, records, UnitExecutionOptions.PackedAnn(options), budget);
        var elapsed = clock.GetTimestamp() - started;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
        return (state, elapsed, allocated, clock.TimestampFrequency);
    }

    internal static void Write((PackedAnnState State, long ElapsedTicks, long AllocatedBytes, long TimestampFrequency) measured,
        int[][][] adjacency, PackedAnnOptions options, AnnWorkBudget budget)
    {
        var state = measured.State;
        var hash = PackedAnnReciprocalGraphHash.Compute(state, adjacency);
        TestContext.Current!.Output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Prefix} rows={state.Count} dimension={state.Space.Dimension} metric={state.Space.Metric} connections={options.Connections} efConstruction={options.EfConstruction} efSearch={options.EfSearch} maxLevel={options.MaxLevel} exactThreshold={options.ExactThreshold} maxRecords={options.MaxRecords} maxIndexBytes={options.MaxIndexBytes} maxScratchBytes={options.MaxScratchBytes} seed={options.Seed} outcome=Success graphSha256={hash} stopwatchFrequency={measured.TimestampFrequency} elapsedStopwatchTicks={measured.ElapsedTicks} allocatedBytes={measured.AllocatedBytes} workUnits={budget.WorkUnits} distanceEvaluations={budget.DistanceEvaluations} edgeVisits={budget.EdgeVisits}"));
    }
}
