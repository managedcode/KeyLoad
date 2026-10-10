namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkCanonicalRollbackTests
{
    [Test]
    public async Task AcChunk008012FailedWholeAtomicAppendAndSealRetainsOriginalOutcomeThenHealthy()
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.Open();
        fixture.AppendInitial();
        var raw = fixture.Raw();
        var originalRows = Convert.ToHexString(JsonDefaults.Serialize(fixture.Read().Records));
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, fixture.Owner.Partition,
        [new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags),
         new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.FirstRevision)]);
        var failed = fixture.Owner.Submit(OperationKind.Batch, request, id: id);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(failed.Json).IsNull();
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(fixture.Read().Records))).IsEqualTo(originalRows);
        var image = fixture.Image();
        var position = fixture.Owner.Store.Position;
        var replay = fixture.Owner.Submit(OperationKind.Batch, request, id: id);
        await Assert.That(SampleRollupWholeFlow.Outcome(replay)).IsEqualTo(SampleRollupWholeFlow.Outcome(failed));
        await Assert.That(fixture.Image()).IsEqualTo(image);
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.AppendedRevision, SampleChunkCanonicalFixture.NoGeneration, SampleChunkCanonicalFixture.InitialSequence);
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision));
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags));
        fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.CorrectedRevision));
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.MergedRevision, SampleChunkCanonicalFixture.MergedGeneration, SampleChunkCanonicalFixture.CorrectedSequence, true);
    }

    [Test]
    public async Task AcChunk007EnrollmentRejectsExistingRawRangeAndRetainsAllRowsBeforeHealthyEmptyWindow()
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.AppendInitial();
        var raw = fixture.Raw();
        var mutation = new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until);
        var id = Guid.NewGuid();
        var failed = fixture.Commit(id, mutation);
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(failed.Json).IsNull();
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await SampleChunkCanonicalAssertions.Replay(fixture, id, mutation, failed);
        var next = mutation with
        {
            WindowId = Guid.NewGuid(),
            From = SampleChunkCanonicalFixture.Until,
            Until = SampleChunkCanonicalFixture.Until.AddHours(SampleChunkCanonicalFixture.FirstRevision)
        };
        fixture.Commit(next);
        var actual = fixture.Owner.Database.ReadSampleChunkWindow(SampleChunkCanonicalFixture.Principal,
            fixture.Request with { WindowId = next.WindowId });
        var expected = new SampleChunkWindowResult(next.WindowId, next.From, next.Until, SampleChunkCanonicalFixture.NoGeneration, SampleChunkCanonicalFixture.FirstRevision, SampleChunkCanonicalFixture.InitialSequence, null,
            [], fixture.Owner.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
    }
}
