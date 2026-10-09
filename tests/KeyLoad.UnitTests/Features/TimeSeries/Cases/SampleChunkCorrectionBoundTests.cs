using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkCorrectionBoundTests
{
    private const int CorrectionBound = 256;
    private const int FirstOrdinal = 1;
    private const long AppendedRevision = 3;
    private const long SealedRevision = 4;
    private const int InitialSequence = 2;
    private const long SealedGeneration = 1;
    private const long MergedGeneration = 2;
    private const string IdPrefix = "bound-correction-";
    private const string ExtraId = "bound-correction-overflow";

    [Test]
    public async Task AcChunk008012ExactCorrectionCapRejectsOneOverThenActualMergeFreesCapacity()
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.Open(); fixture.AppendInitial();
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, AppendedRevision));
        var corrections = Enumerable.Range(FirstOrdinal, CorrectionBound).Select(ordinal =>
            new SampleData(IdPrefix + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SampleChunkCanonicalFixture.Start.AddTicks(ordinal), ordinal)).ToImmutableArray();
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, corrections, SampleChunkCanonicalFixture.Tags));
        var revision = SealedRevision + CorrectionBound;
        var rows = SampleChunkCanonicalFixture.Expected().Concat(corrections.Select((sample, index) => new SampleRecord(
            SampleChunkCanonicalFixture.Series, sample, InitialSequence + FirstOrdinal + index,
            SampleChunkCanonicalFixture.Tags))).ToImmutableArray();
        await LiteralAsync(fixture, revision, SealedGeneration, rows);
        var overflow = new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [new(ExtraId, SampleChunkCanonicalFixture.Start.AddTicks(CorrectionBound + FirstOrdinal), FirstOrdinal)],
            SampleChunkCanonicalFixture.Tags);
        var raw = fixture.Raw(); var originalId = Guid.NewGuid();
        var rejected = fixture.Commit(originalId, overflow);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(rejected.Json).IsNull();
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await LiteralAsync(fixture, revision, SealedGeneration, rows);
        await SampleChunkCanonicalAssertions.Replay(fixture, originalId, overflow, rejected);
        fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, revision));
        await LiteralAsync(fixture, revision + FirstOrdinal, MergedGeneration, rows);
        fixture.Commit(overflow);
        var expected = rows.Add(new(SampleChunkCanonicalFixture.Series, overflow.Samples[0],
            InitialSequence + CorrectionBound + FirstOrdinal, SampleChunkCanonicalFixture.Tags));
        await LiteralAsync(fixture, revision + FirstOrdinal + FirstOrdinal, MergedGeneration, expected);
        await SampleChunkCanonicalAssertions.Replay(fixture, originalId, overflow, rejected);
        await LiteralAsync(fixture, revision + FirstOrdinal + FirstOrdinal, MergedGeneration, expected);
    }

    private static async Task LiteralAsync(SampleChunkCanonicalFixture fixture, long revision,
        long generation, ImmutableArray<SampleRecord> rows)
    {
        var request = fixture.Request with { Limit = CorrectionBound + InitialSequence + FirstOrdinal };
        var actual = fixture.Owner.Database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request);
        var expected = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, generation, revision, rows.Length, null, rows,
            fixture.Owner.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }
}
