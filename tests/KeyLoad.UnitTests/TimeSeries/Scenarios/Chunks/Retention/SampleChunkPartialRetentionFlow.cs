using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkPartialRetentionFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        try
        {
            using var fixture = new SampleChunkPartialRetentionFixture();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(fixture), failures);
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(SampleChunkPartialRetentionFixture fixture)
    {
        fixture.Commit(new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until));
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags));
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision));
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkPartialRetentionFixture.LateTags));
        var manifestKey = SampleChunkKeys.Manifest(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.FirstGeneration);
        var blockKey = SampleChunkKeys.Block(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.FirstGeneration,
            SampleChunkCanonicalFixture.FirstIndex);
        var manifest = SampleChunkPartialRetentionAssertions.Bytes(fixture, manifestKey);
        var block = SampleChunkPartialRetentionAssertions.Bytes(fixture, blockKey);
        fixture.Clock.AdvanceTo(SampleChunkCanonicalFixture.Late.Timestamp);
        fixture.Commit(new ExpireSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Late.Timestamp, SampleChunkCanonicalFixture.OutputLimit));
        await Assert.That(SampleChunkPartialRetentionAssertions.Bytes(fixture, manifestKey).SequenceEqual(manifest)).IsTrue();
        await Assert.That(SampleChunkPartialRetentionAssertions.Bytes(fixture, blockKey).SequenceEqual(block)).IsTrue();
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged: false);
        await SampleChunkRetainedBudgetTrial.RunAsync(fixture, merged: false);
        fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.CorrectedRevision));
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged: true);
        await SampleChunkRetainedBudgetTrial.RunAsync(fixture, merged: true);
        await DuplicateAndRefusal(fixture);
        await Cold(fixture, fresh: false);
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkPartialRetentionFixture.Fresh], SampleChunkCanonicalFixture.Tags));
        fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.MergedRevision + SampleChunkCanonicalFixture.FirstRevision));
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged: true, fresh: true);
        await SampleChunkRetainedBudgetTrial.RunAsync(fixture, merged: true, fresh: true);
        await Cold(fixture, fresh: true);
    }

    private static async Task DuplicateAndRefusal(SampleChunkPartialRetentionFixture fixture)
    {
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags));
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged: true);
        var changed = SampleChunkCanonicalFixture.Initial[SampleChunkCanonicalFixture.FirstIndex]
            with
        {
            Value = SampleChunkPartialRetentionFixture.FreshValue
        };
        var failure = fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, [changed], SampleChunkCanonicalFixture.Tags)));
        await Assert.That(Errors.Code(failure.Problem)).IsEqualTo(ErrorCode.Conflict);
        
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged: true);
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Read(cancellation.Token));
        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        await SampleChunkPartialRetentionAssertions.Replay(fixture);
    }

    private static async Task Cold(SampleChunkPartialRetentionFixture fixture, bool fresh)
    {
        var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        await SampleChunkPartialRetentionAssertions.Healthy(fixture, merged: true, fresh: fresh);
        await SampleChunkRetainedBudgetTrial.RunAsync(fixture, merged: true, fresh: fresh);
        await SampleChunkPartialRetentionAssertions.Replay(fixture);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
    }
}
