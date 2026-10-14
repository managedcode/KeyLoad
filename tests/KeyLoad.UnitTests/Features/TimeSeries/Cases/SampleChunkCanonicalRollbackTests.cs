namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkCanonicalRollbackTests
{
    [Test]
    public async Task AcChunk008012FailedWholeAtomicAppendAndSealRetainsOriginalOutcomeThenHealthy()
        => await SampleChunkAtomicRollbackFlow.RunAsync();

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
            fixture.Request with
            {
                WindowId = next.WindowId
            });
        var expected = new SampleChunkWindowResult(next.WindowId, next.From, next.Until, SampleChunkCanonicalFixture.NoGeneration, SampleChunkCanonicalFixture.FirstRevision, SampleChunkCanonicalFixture.InitialSequence, null,
            [], fixture.Owner.Store.Position);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
    }
}
