namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleChunkCanonicalLifecycleTests
{
    [Test]
    public async Task AcChunk007008009ExactOpenSealLateCorrectionMergeAndReceiptReplay()
        => await SampleChunkCorrectionTagFlow.RunAsync();

    [Test]
    public async Task AcChunk010OriginalCancellationPreservesFullCutThenHealthyExactWindow()
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.Open();
        fixture.AppendInitial();
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision));
        var image = fixture.Image();
        var position = fixture.Owner.Store.Position;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Read(cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(fixture.Image()).IsEqualTo(image);
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.SealedRevision, SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.InitialSequence);
    }
}
