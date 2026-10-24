namespace KeyLoad.UnitTests.TimeSeries;

internal static class ChunkWatermarkGapTrial
{
    internal static readonly SampleData FirstOutside = new("first-outside", SampleChunkCanonicalFixture.Until.AddTicks(1), 16);
    internal static readonly SampleData SecondOutside = new("second-outside", SampleChunkCanonicalFixture.Until.AddTicks(2), 32);

    internal static async Task Run()
    {
        using var fixture = new ChunkSelectedIntegrityFixture();
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false);
        Append(fixture, FirstOutside);
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false);
        var merge = fixture.Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.CorrectedRevision));
        fixture.Apply(merge);
        await ChunkWatermarkGapAssertions.Healthy(fixture, second: false);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        var firstCold = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, firstCold);
        await ChunkWatermarkGapAssertions.Healthy(fixture, second: false);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        Append(fixture, SecondOutside);
        await ChunkWatermarkGapAssertions.Healthy(fixture, second: true);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        var secondCold = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, secondCold);
        await ChunkWatermarkGapAssertions.Healthy(fixture, second: true);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, secondCold);
    }

    private static void Append(ChunkSelectedIntegrityFixture fixture, SampleData sample)
        => fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, [sample], SampleChunkCanonicalFixture.Tags)));
}
