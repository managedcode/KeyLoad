using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockReadBudgetCalls
{
    private const int UnboundedRecords = int.MaxValue;

    internal static ReadExecutionBudget Budget(ChunkSelectedIntegrityFixture fixture, long maximum)
    {
        var budget = new ReadExecutionBudget(fixture.Owner.EvaluationClock);
        budget.ConstrainWork(maximum, UnboundedRecords);
        return budget;
    }

    internal static SampleRecord[] Read(ChunkSelectedIntegrityFixture fixture, ReadExecutionBudget budget)
        => fixture.Store.Read(original =>
        {
            var options = UnitExecutionOptions.TimeSeriesExecution();
            var window = SampleChunkStorage.Require<SampleChunkWindow>(original,
                SampleChunkKeys.Window(fixture.Partition, SampleChunkCanonicalFixture.Set,
                    SampleChunkCanonicalFixture.Series, fixture.WindowId));
            var manifest = SampleChunkManifestReader.Read(original, fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, window,
                options.Value.MaximumChunkWindowRecords, options);
            return SampleChunkGenerationReader.Read(budget.CreateView(original), fixture.Partition,
                SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, window, manifest,
                options, SampleChunkWork.Observe(budget));
        });
}
