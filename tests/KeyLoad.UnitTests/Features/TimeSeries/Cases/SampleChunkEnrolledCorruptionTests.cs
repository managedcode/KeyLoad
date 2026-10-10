using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkEnrolledCorruptionTests
{
    [Test]
    [Arguments(SampleChunkCorruptionCut.MissingManifest)]
    [Arguments(SampleChunkCorruptionCut.ManifestDigest)]
    [Arguments(SampleChunkCorruptionCut.ManifestVersion)]
    [Arguments(SampleChunkCorruptionCut.ManifestGeneration)]
    [Arguments(SampleChunkCorruptionCut.MissingBlock)]
    [Arguments(SampleChunkCorruptionCut.BlockDigest)]
    [Arguments(SampleChunkCorruptionCut.MissingCorrection)]
    [Arguments(SampleChunkCorruptionCut.CorrectionSequence)]
    public async Task AcChunk010012EnrolledNativeCorruptionRefusesWholeReadThenExactRepairReceiptAndMergeAreHealthy(SampleChunkCorruptionCut cut)
    {
        using var fixture = new SampleChunkCanonicalFixture();
        fixture.Open();
        fixture.AppendInitial();
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision));
        var originalId = Guid.NewGuid();
        var originalMutation = new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags);
        var original = fixture.Commit(originalId, originalMutation);
        original.Get<CommitReceipt>();
        var raw = fixture.Raw();
        var image = new SampleChunkCorruptImage(fixture);
        var failures = new List<Exception>();
        try
        {
            var expected = image.Apply(cut);
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCanonicalAssertions.ReadDenial(fixture,
                () => fixture.Read(TestContext.Current!.Execution.CancellationToken), expected), failures);
        }
        finally { ServerFailureObserver.Observe(image.Restore, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.CorrectedRevision,
            SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.CorrectedSequence, late: true);
        await SampleChunkCanonicalAssertions.Replay(fixture, originalId, originalMutation, original);
        fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.CorrectedRevision));
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.MergedRevision,
            SampleChunkCanonicalFixture.MergedGeneration, SampleChunkCanonicalFixture.CorrectedSequence, late: true);
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await SampleChunkCanonicalAssertions.Replay(fixture, originalId, originalMutation, original);
    }
}
