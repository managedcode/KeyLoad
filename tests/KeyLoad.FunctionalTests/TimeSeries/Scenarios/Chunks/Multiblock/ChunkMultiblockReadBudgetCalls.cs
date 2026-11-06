using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockReadBudgetCalls
{

    internal static SampleRecord[] Read(ChunkSelectedIntegrityFixture fixture, ReadExecutionBudget budget, Action? afterPointRead = null)
        => fixture.Store.Read(original =>
        {
            var options = UnitExecutionOptions.TimeSeriesExecution();
            var window = SampleChunkStorage.Require<SampleChunkWindow>(original,
                SampleChunkKeys.Window(fixture.Partition, SampleChunkCanonicalFixture.Set,
                    SampleChunkCanonicalFixture.Series, fixture.WindowId));
            var manifest = SampleChunkManifestReader.Read(original, fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, window,
                options.Value.MaximumChunkWindowRecords, options);
            var observed = afterPointRead is null ? original : new PointObservedView(original, afterPointRead);
            return SampleChunkGenerationReader.Read(budget.CreateView(observed), fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, window, manifest,
                options, SampleChunkWork.Observe(budget));
        });
}
