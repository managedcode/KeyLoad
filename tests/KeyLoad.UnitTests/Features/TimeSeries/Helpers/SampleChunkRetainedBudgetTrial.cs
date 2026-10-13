namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkRetainedBudgetTrial
{
    private const long OneByte = 1;
    private const int OneRecord = 1;
    internal static async Task RunAsync(SampleChunkPartialRetentionFixture fixture, bool merged, bool fresh = false)
    {
        var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var original = Read(fixture, fixture.Owner.Limits.MaxQueryReadBytes, fixture.Owner.Limits.MaxScanRecords);
        await SampleChunkRetainedBudgetExpected.RequireAsync(fixture, original.Result, merged, fresh);
        await Assert.That(original.Bytes > OneByte).IsTrue();
        await Assert.That(original.Records > OneRecord).IsTrue();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        var exact = Read(fixture, original.Bytes, original.Records);
        await SampleChunkRetainedBudgetExpected.RequireAsync(fixture, exact.Result, merged, fresh);
        await Assert.That(exact.Bytes).IsEqualTo(original.Bytes);
        await Assert.That(exact.Records).IsEqualTo(original.Records);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        await RefuseAsync(fixture, original.Bytes - OneByte, fixture.Owner.Limits.MaxScanRecords,
            precedingRead: false, merged, fresh);
        await RefuseAsync(fixture, fixture.Owner.Limits.MaxQueryReadBytes, original.Records - OneRecord,
            precedingRead: false, merged, fresh);
        await RefuseAsync(fixture, original.Bytes, fixture.Owner.Limits.MaxScanRecords,
            precedingRead: true, merged, fresh);
        var cumulative = Read(fixture, fixture.Owner.Limits.MaxQueryReadBytes,
            fixture.Owner.Limits.MaxScanRecords, precedingRead: true);
        await SampleChunkRetainedBudgetExpected.RequireAsync(fixture, cumulative.Result, merged, fresh);
        await Assert.That(cumulative.Bytes > original.Bytes).IsTrue();
        await Assert.That(cumulative.Records).IsEqualTo(original.Records + OneRecord);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        var exactCumulative = Read(fixture, cumulative.Bytes, cumulative.Records, precedingRead: true);
        await SampleChunkRetainedBudgetExpected.RequireAsync(fixture, exactCumulative.Result, merged, fresh);
        await Assert.That(exactCumulative.Bytes).IsEqualTo(cumulative.Bytes);
        await Assert.That(exactCumulative.Records).IsEqualTo(cumulative.Records);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged, fresh);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
    }

    private static SampleChunkRetainedBudgetObservation Read(SampleChunkPartialRetentionFixture fixture,
        long maximumBytes, int maximumRecords, bool precedingRead = false)
    {
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(
            UnitExecutionOptions.DatabaseLimits(fixture.Owner.Limits), fixture.Clock);
        budget.ConstrainNativeWork(maximumBytes, maximumRecords);
        var result = fixture.Store.Read(view =>
        {
            if (precedingRead)
            {
                _ = budget.Read(view, global::KeyLoad.Core.Features.TimeSeries.SampleChunkKeys.Window(
                    fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, fixture.WindowId));
            }
            var request = new ReadSampleChunkWindowRequest(fixture.Partition, SampleChunkCanonicalFixture.Set,
                SampleChunkCanonicalFixture.Series, fixture.WindowId, null, null, SampleChunkCanonicalFixture.OutputLimit);
            return global::KeyLoad.Core.Features.TimeSeries.SampleChunkWindowReader.Read(fixture.Owner,
                budget.CreateView(view), SampleChunkCanonicalFixture.Principal, request, budget,
                UnitExecutionOptions.TimeSeriesExecution(), fixture.Store.Position);
        });
        return new(result, budget.ReadBytes, budget.ConstrainedNativeRecords);
    }

    private static async Task RefuseAsync(SampleChunkPartialRetentionFixture fixture, long maximumBytes,
        int maximumRecords, bool precedingRead, bool merged, bool fresh)
    {
        var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Read(fixture, maximumBytes, maximumRecords, precedingRead));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(global::KeyLoad.Core.ReadExecutionBudget.ReadBytesExceeded);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged, fresh);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, before);
    }
}
