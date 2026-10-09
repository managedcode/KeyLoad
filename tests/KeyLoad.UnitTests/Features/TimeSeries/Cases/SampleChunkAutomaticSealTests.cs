using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkAutomaticSealTests
{
    private const int One = 1;
    private const double Value = 7;
    private const string Prefix = "automatic-";

    [Test]
    public async Task AcChunk007009FullNativeBlockAutomaticallySealsThenCorrectionMergesWithoutRawRewrite()
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.Open();
        var samples = Enumerable.Range(One, SampleChunkTestData.MaximumRecords)
            .Select(index => new SampleData(Prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SampleChunkCanonicalFixture.Start.AddTicks(index), Value)).ToImmutableArray();
        var receipt = fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, samples, SampleChunkCanonicalFixture.Tags));
        var request = fixture.Request with { Limit = SampleChunkTestData.MaximumRecords + One };
        var actual = fixture.Owner.Database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request);
        var revision = checked(SampleChunkCanonicalFixture.FirstRevision + samples.Length + One);
        var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.FirstGeneration, revision,
            samples.Length, null, [.. samples.Select((sample, index) => new SampleRecord(
                SampleChunkCanonicalFixture.Series, sample, index + One, SampleChunkCanonicalFixture.Tags))],
            receipt.Token.Position);
        await EqualAsync(expected, actual);
        var correction = fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags));
        var corrected = fixture.Owner.Database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request);
        await Assert.That(corrected.CutPosition).IsEqualTo(correction.Token.Position);
        await Assert.That(corrected.CutPosition).IsEqualTo(fixture.Owner.Store.Position);
        var correctedExpected = expected with
        {
            Revision = revision + One,
            SourceSequence = samples.Length + One,
            Records = [.. expected.Records.Append(new SampleRecord(SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Late, samples.Length + One, SampleChunkCanonicalFixture.Tags))
                .OrderBy(row => row.Sample.Timestamp.UtcTicks).ThenBy(row => row.Sequence)],
            CutPosition = corrected.CutPosition
        };
        await EqualAsync(correctedExpected, corrected);
        await Assert.That(corrected.Generation).IsEqualTo(SampleChunkCanonicalFixture.FirstGeneration);
        await Assert.That(corrected.Revision).IsEqualTo(revision + One);
        var raw = fixture.Raw();
        var merge = fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, corrected.Revision));
        var merged = fixture.Owner.Database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request);
        await Assert.That(merged.CutPosition).IsEqualTo(merge.Token.Position);
        await Assert.That(merged.CutPosition).IsEqualTo(fixture.Owner.Store.Position);
        await EqualAsync(correctedExpected with { Generation = SampleChunkCanonicalFixture.MergedGeneration,
            Revision = revision + One + One, CutPosition = merged.CutPosition }, merged);
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
    }

    private static async Task EqualAsync(SampleChunkWindowResult expected, SampleChunkWindowResult actual)
        => await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
}
