namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkCanonicalLifecycleTests
{
    [Test]
    public async Task AcChunk007008009ExactOpenSealLateCorrectionMergeAndReceiptReplay()
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.Open();
        fixture.AppendInitial();
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.AppendedRevision, SampleChunkCanonicalFixture.NoGeneration, SampleChunkCanonicalFixture.InitialSequence);
        var seal = new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision);
        var sealId = Guid.NewGuid();
        var original = fixture.Commit(sealId, seal);
        original.Get<CommitReceipt>();
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.SealedRevision, SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.InitialSequence);
        await SampleChunkCanonicalAssertions.Replay(fixture, sealId, seal, original);
        var late = new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags);
        fixture.Commit(late);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.CorrectedRevision, SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.CorrectedSequence, true);
        fixture.Commit(late);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.CorrectedRevision, SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.CorrectedSequence, true);
        var raw = fixture.Raw();
        var merge = new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.CorrectedRevision);
        var mergeId = Guid.NewGuid();
        var merged = fixture.Commit(mergeId, merge);
        merged.Get<CommitReceipt>();
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.MergedRevision, SampleChunkCanonicalFixture.MergedGeneration, SampleChunkCanonicalFixture.CorrectedSequence, true);
        await SampleChunkCanonicalAssertions.Replay(fixture, mergeId, merge, merged);
    }

    [Test]
    public async Task AcChunk010OriginalCancellationPreservesFullCutThenHealthyExactWindow()
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.Open(); fixture.AppendInitial();
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision));
        var image = fixture.Image(); var position = fixture.Owner.Store.Position;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Read(cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(fixture.Image()).IsEqualTo(image);
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.SealedRevision, SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.InitialSequence);
    }
}
