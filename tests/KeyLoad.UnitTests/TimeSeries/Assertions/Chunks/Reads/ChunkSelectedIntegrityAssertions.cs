using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkSelectedIntegrityAssertions
{
    internal static async Task Raw(ChunkSelectedIntegrityFixture fixture, bool final)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var expected = Expected(final);
        var rows = fixture.Owner.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit);
        await Rows(rows, expected);
        var latest = TimeSeriesReadOperations.ReadLatestSample(fixture.Owner, SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Until));
        await Rows([latest.Sample!], [expected[^1]]);
        var aggregate = final ? new SampleAggregate(4, 14, -0d, 8, 3.5) : new(3, 6, -0d, 4, 2);
        await Assert.That(TimeSeriesReadOperations.AggregateSamples(fixture.Owner, SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until))).IsEqualTo(aggregate);
        var windows = TimeSeriesReadOperations.AggregateSampleWindows(fixture.Owner, SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until, TimeSpan.FromHours(1)));
        await Assert.That(windows.Windows.Length).IsEqualTo(1);
        await Assert.That(windows.Windows[0]).IsEqualTo(new SampleAggregateWindow(
            SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until, aggregate));
        var unrelated = fixture.Owner.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Partition,
            SampleChunkCanonicalFixture.Set, ChunkSelectedIntegrityFixture.OtherSeries, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit);
        await Rows(unrelated, [new(ChunkSelectedIntegrityFixture.OtherSeries, ChunkSelectedIntegrityFixture.Other,
            1, SampleChunkCanonicalFixture.Tags)]);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }

    internal static async Task Healthy(ChunkSelectedIntegrityFixture fixture, bool final, long? generation = null, long? revision = null)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var actual = fixture.Owner.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
            new(fixture.Partition, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                fixture.WindowId, null, null, SampleChunkCanonicalFixture.OutputLimit));
        var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, generation ?? (final ? 3 : 1), revision ?? (final ? 8 : 5), final ? 4 : 3, null,
            [.. Expected(final)], fixture.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        await Raw(fixture, final);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }

    internal static async Task Replay(ChunkSelectedIntegrityFixture fixture)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        foreach (var original in fixture.Successes)
        {
            var replay = fixture.Owner.Apply(original.Operation).Get<CommitReceipt>();
            await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(original.Receipt))).IsTrue();
            await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        }
    }

    private static SampleRecord[] Expected(bool final) => final
        ? [.. SampleChunkCanonicalFixture.Expected(late: true), new(SampleChunkCanonicalFixture.Series,
            ChunkSelectedIntegrityFixture.Fresh, 4, SampleChunkCanonicalFixture.Tags)]
        : SampleChunkCanonicalFixture.Expected(late: true);

    private static async Task Rows(SampleRecord[] actual, SampleRecord[] expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(OrleansSerialization.Serialize(actual[index]).SequenceEqual(OrleansSerialization.Serialize(expected[index]))).IsTrue();
        }
    }
}
