using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkMultiblockReadBudgetCalls
{
    internal static ReadExecutionBudget Budget(ChunkSelectedIntegrityFixture fixture, long maximum)
        => new(UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxQueryReadBytes = checked((int)maximum)
        }),
            fixture.Owner.EvaluationClock);

    internal static SampleRecord[] Read(ChunkSelectedIntegrityFixture fixture, ReadExecutionBudget budget)
        => fixture.Store.Read(original =>
        {
            var options = UnitExecutionOptions.TimeSeriesExecution();
            var headerCharge = new SampleChunkReadCharge(fixture.Owner.Limits.MaxQueryReadBytes);
            var window = SampleChunkStorage.Require<SampleChunkWindow>(original,
                SampleChunkKeys.Window(fixture.Partition, SampleChunkCanonicalFixture.Set,
                    SampleChunkCanonicalFixture.Series, fixture.WindowId), headerCharge);
            var manifest = SampleChunkManifestReader.Read(original, fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, window,
                options.Value.MaximumChunkWindowRecords, headerCharge, options);
            return SampleChunkGenerationReader.Read(budget.CreateView(original), fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, window, manifest,
                options, new SampleChunkReadCharge(fixture.Owner.Limits.MaxQueryReadBytes), SampleChunkWork.Observe(budget));
        });
}
