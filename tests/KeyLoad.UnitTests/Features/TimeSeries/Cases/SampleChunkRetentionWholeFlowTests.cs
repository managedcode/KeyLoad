namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkRetentionWholeFlowTests
{
    private const long AppendedRevision = 3;
    private const long SealedRevision = 4;
    private const long SealedGeneration = 1;
    private const long SourceSequence = 2;
    private const long NewWindowRevision = 2;
    private const long FreshSequence = 3;
    private const long OpenGeneration = 0;
    private const int WindowHours = 1;
    private const string FreshEventId = "post-retention-chunk";

    [Test]
    public async Task AcChunk011012ActualRetentionFloorDropAndOriginalReceiptRemainFencedBeforeFreshWindow()
    {
        var clock = new SampleChunkRetentionClock(SampleChunkCanonicalFixture.Start);
        using var fixture = new SampleChunkCanonicalFixture(timeProvider: clock);
        fixture.Open(); fixture.AppendInitial();
        var sealId = Guid.NewGuid();
        var seal = new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, AppendedRevision);
        var acknowledged = fixture.Commit(sealId, seal);
        acknowledged.Get<CommitReceipt>();
        clock.AdvanceTo(SampleChunkCanonicalFixture.Until);
        fixture.Commit(new ExpireSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.OutputLimit));
        var retained = new SampleChunkWindowResult(fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SealedGeneration, SealedRevision, SourceSequence,
            SampleChunkCanonicalFixture.Until.UtcTicks, [], fixture.Owner.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(fixture.Read())))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(retained)));
        fixture.Commit(new DropSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SealedRevision));
        await SampleChunkCanonicalAssertions.ReadDenial(fixture, () => fixture.Read(), ErrorCode.HistoryUnavailable);
        await SampleChunkCanonicalAssertions.Replay(fixture, sealId, seal, acknowledged);
        var sameId = fixture.Commit(Guid.NewGuid(), new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.Until,
            SampleChunkCanonicalFixture.Until.AddHours(WindowHours)));
        await Assert.That(sameId.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(sameId.Json).IsNull();
        var freshId = Guid.NewGuid();
        fixture.Commit(new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, freshId, SampleChunkCanonicalFixture.Until,
            SampleChunkCanonicalFixture.Until.AddHours(WindowHours)));
        var sample = new SampleData(FreshEventId, SampleChunkCanonicalFixture.Until, FreshSequence);
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [sample], SampleChunkCanonicalFixture.Tags));
        var request = fixture.Request with { WindowId = freshId };
        var expected = new SampleChunkWindowResult(freshId, SampleChunkCanonicalFixture.Until,
            SampleChunkCanonicalFixture.Until.AddHours(WindowHours), OpenGeneration, NewWindowRevision,
            FreshSequence, SampleChunkCanonicalFixture.Until.UtcTicks,
            [new(SampleChunkCanonicalFixture.Series, sample, FreshSequence, SampleChunkCanonicalFixture.Tags)],
            fixture.Owner.Store.Position);
        var actual = fixture.Owner.Database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal, request);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        await SampleChunkCanonicalAssertions.ReadDenial(fixture, () => fixture.Read(), ErrorCode.HistoryUnavailable);
    }
}
