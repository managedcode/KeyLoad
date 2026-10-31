namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleChunkRetainedBudgetTrial
{
    internal static async Task RunAsync(SampleChunkPartialRetentionFixture fixture, bool merged, bool fresh = false)
    {
        var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
        await SampleChunkRetainedBudgetExpected.RequireAsync(fixture, Read(fixture), merged, fresh);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        await SampleChunkRetainedBudgetExpected.RequireAsync(fixture, Read(fixture, precedingRead: true), merged, fresh);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged, fresh);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
    }

    private static SampleChunkWindowResult Read(SampleChunkPartialRetentionFixture fixture, bool precedingRead = false)
    {
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(fixture.Clock);
        return fixture.Store.Read(view =>
        {
            if (precedingRead)
            {
                _ = budget.Read(view, global::KeyLoad.Core.TimeSeries.SampleChunkKeys.Window(
                    fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, fixture.WindowId));
            }
            var request = new ReadSampleChunkWindowRequest(fixture.Partition, SampleChunkCanonicalFixture.Set,
                SampleChunkCanonicalFixture.Series, fixture.WindowId, null, null, SampleChunkCanonicalFixture.OutputLimit);
            return global::KeyLoad.Core.TimeSeries.SampleChunkWindowReader.Read(fixture.Owner,
                budget.CreateView(view), SampleChunkCanonicalFixture.Principal, request, budget,
                UnitExecutionOptions.TimeSeriesExecution(), fixture.Store.Position);
        });
    }
}
