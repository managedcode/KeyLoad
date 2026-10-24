using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleChunkCorrectionTagFlow
{
    private const long TaggedSequence = 4;
    private const long TaggedRevision = 7;
    private const long TaggedMergeRevision = 8;

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SampleChunkCanonicalFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(SampleChunkCanonicalFixture fixture)
    {
        var originals = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        await CommitAsync(fixture, new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until), "openSampleChunkWindow", SampleChunkCanonicalFixture.FirstRevision, originals);
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags), "appendSamples", SampleChunkCanonicalFixture.InitialSequence, originals);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.AppendedRevision,
            SampleChunkCanonicalFixture.NoGeneration, SampleChunkCanonicalFixture.InitialSequence);
        await CommitAsync(fixture, new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision),
            "sealSampleChunkWindow", SampleChunkCanonicalFixture.SealedRevision, originals);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.SealedRevision,
            SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.InitialSequence);
        await SampleChunkCorrectionTagCold.ReplayAsync(fixture.Owner.Database, fixture.Owner.Store, originals);
        var late = new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags);
        await CommitAsync(fixture, late, "appendSamples", SampleChunkCanonicalFixture.CorrectedSequence, originals);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.CorrectedRevision,
            SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.CorrectedSequence, late: true);
        await CommitAsync(fixture, late, "appendSamples", SampleChunkCanonicalFixture.CorrectedSequence, originals);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.CorrectedRevision,
            SampleChunkCanonicalFixture.FirstGeneration, SampleChunkCanonicalFixture.CorrectedSequence, late: true);
        var raw = fixture.Raw();
        await CommitAsync(fixture, new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.CorrectedRevision),
            "mergeSampleChunkWindow", SampleChunkCanonicalFixture.MergedRevision, originals);
        await Assert.That(fixture.Raw()).IsEqualTo(raw);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.MergedRevision,
            SampleChunkCanonicalFixture.MergedGeneration, SampleChunkCanonicalFixture.CorrectedSequence, late: true);
        await SampleChunkCorrectionTagCold.ReplayAsync(fixture.Owner.Database, fixture.Owner.Store, originals);
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCorrectionTagRows.Tagged], SampleChunkCorrectionTagRows.TaggedJson), "appendSamples", TaggedSequence, originals);
        await SampleChunkCorrectionTagRows.RunAsync(fixture.Owner.Database, fixture.Owner.Store, fixture, merged: false);
        await SampleChunkCorrectionTagCold.ReplayAsync(fixture.Owner.Database, fixture.Owner.Store, originals);
        await CommitAsync(fixture, new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, TaggedRevision), "mergeSampleChunkWindow", TaggedMergeRevision, originals);
        await SampleChunkCorrectionTagCold.RunAsync(fixture, originals);
    }

    private static Task CommitAsync(SampleChunkCanonicalFixture fixture, Mutation mutation, string kind, long revision,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
        => SampleChunkCorrectionBoundReceipts.CommitAsync(fixture, mutation, kind, revision, originals);
}
