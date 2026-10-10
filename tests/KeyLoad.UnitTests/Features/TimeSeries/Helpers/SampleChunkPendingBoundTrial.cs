namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkPendingBoundTrial(SampleChunkCanonicalFixture fixture)
{
    internal Guid SecondWindow { get; } = Guid.NewGuid();
    private static DateTimeOffset SecondStart => SampleChunkCanonicalFixture.Until;
    private static DateTimeOffset SecondEnd => SecondStart.AddHours(SampleChunkPendingBoundProtocol.WindowHours);
    private static SampleData First => new(SampleChunkPendingBoundProtocol.FirstId,
        SampleChunkCanonicalFixture.Start, SampleChunkPendingBoundProtocol.FirstValue);
    private static SampleData Second => new(SampleChunkPendingBoundProtocol.SecondId, SecondStart,
        SampleChunkPendingBoundProtocol.SecondValue);
    private static SampleData FirstLate => new(SampleChunkPendingBoundProtocol.FirstLateId,
        SampleChunkCanonicalFixture.Start.AddTicks(SampleChunkPendingBoundProtocol.FirstSequence),
        SampleChunkPendingBoundProtocol.LateValue);
    private static SampleData SecondLate => new(SampleChunkPendingBoundProtocol.SecondLateId,
        SecondStart.AddTicks(SampleChunkPendingBoundProtocol.FirstSequence), SampleChunkPendingBoundProtocol.LateValue);
    internal static AppendSamples Overflow => Append(SecondLate);

    internal void Seed()
    {
        fixture.Open();
        fixture.Commit(new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SecondWindow, SecondStart, SecondEnd));
        fixture.Commit(Append(First));
        fixture.Commit(Append(Second));
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkPendingBoundProtocol.Appended));
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SecondWindow, SampleChunkPendingBoundProtocol.Appended));
        fixture.Commit(Append(FirstLate));
    }

    internal async Task RequireAsync(bool firstMerged, bool secondCorrected)
    {
        var firstRows = new SampleRecord[] { Row(First, SampleChunkPendingBoundProtocol.FirstSequence),
            Row(FirstLate, SampleChunkPendingBoundProtocol.ThirdSequence) };
        var secondRows = secondCorrected
            ? new SampleRecord[] { Row(Second, SampleChunkPendingBoundProtocol.SecondSequence),
                Row(SecondLate, SampleChunkPendingBoundProtocol.FourthSequence) }
            : [Row(Second, SampleChunkPendingBoundProtocol.SecondSequence)];
        await WindowAsync(fixture.WindowId, SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until,
            firstMerged ? SampleChunkPendingBoundProtocol.MergedGeneration : SampleChunkPendingBoundProtocol.Generation,
            firstMerged ? SampleChunkPendingBoundProtocol.Merged : SampleChunkPendingBoundProtocol.Corrected,
            SampleChunkPendingBoundProtocol.ThirdSequence, firstRows);
        await WindowAsync(SecondWindow, SecondStart, SecondEnd, SampleChunkPendingBoundProtocol.Generation,
            secondCorrected ? SampleChunkPendingBoundProtocol.Corrected : SampleChunkPendingBoundProtocol.Sealed,
            secondCorrected ? SampleChunkPendingBoundProtocol.FourthSequence : SampleChunkPendingBoundProtocol.SecondSequence,
            secondRows);
        var raw = fixture.Owner.Database.ReadSamples(SampleChunkCanonicalFixture.Principal, fixture.Owner.Partition,
            SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Start,
            SecondEnd, SampleChunkCanonicalFixture.OutputLimit);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(raw)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(firstRows.Concat(secondRows).ToArray())));
    }

    private async Task WindowAsync(Guid id, DateTimeOffset from, DateTimeOffset until, long generation,
        long revision, long sequence, SampleRecord[] rows)
    {
        var request = fixture.Request with { WindowId = id };
        var actual = fixture.Owner.Database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request);
        var expected = new SampleChunkWindowResult(id, from, until, generation, revision, sequence, null,
            [.. rows], fixture.Owner.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }

    private static AppendSamples Append(SampleData sample) => new(SampleChunkCanonicalFixture.Set,
        SampleChunkCanonicalFixture.Series, [sample], SampleChunkCanonicalFixture.Tags);
    private static SampleRecord Row(SampleData sample, long sequence) => new(SampleChunkCanonicalFixture.Series,
        sample, sequence, SampleChunkCanonicalFixture.Tags);
}
