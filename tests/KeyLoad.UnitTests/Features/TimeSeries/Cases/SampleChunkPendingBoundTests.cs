namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkPendingBoundTests
{
    [Test]
    public async Task AcChunk008009012SameSeriesPendingRefusalReplaysThenActualMergeFreesHealthyCorrection()
    {
        using var fixture = new SampleChunkCanonicalFixture(new()
        {
            MaximumPendingChunkWindows = SampleChunkPendingBoundProtocol.Pending,
            MaximumChunkWindows = SampleChunkPendingBoundProtocol.Windows
        });
        var trial = new SampleChunkPendingBoundTrial(fixture);
        trial.Seed();
        await trial.RequireAsync(firstMerged: false, secondCorrected: false);
        var mutation = SampleChunkPendingBoundTrial.Overflow;
        var originalId = Guid.NewGuid();
        var raw = fixture.Raw();
        var rejected = fixture.Commit(originalId, mutation);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(rejected.Json).IsNull();
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await trial.RequireAsync(firstMerged: false, secondCorrected: false);
        await SampleChunkCanonicalAssertions.Replay(fixture, originalId, mutation, rejected);
        var merge = fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkPendingBoundProtocol.Corrected));
        await Assert.That(merge.Token.Position).IsEqualTo(fixture.Owner.Store.Position);
        await trial.RequireAsync(firstMerged: true, secondCorrected: false);
        var healthy = fixture.Commit(mutation);
        await Assert.That(healthy.Token.Position > merge.Token.Position).IsTrue();
        await Assert.That(healthy.Token.Position).IsEqualTo(fixture.Owner.Store.Position);
        await trial.RequireAsync(firstMerged: true, secondCorrected: true);
        await SampleChunkCanonicalAssertions.Replay(fixture, originalId, mutation, rejected);
        await trial.RequireAsync(firstMerged: true, secondCorrected: true);
    }
}
