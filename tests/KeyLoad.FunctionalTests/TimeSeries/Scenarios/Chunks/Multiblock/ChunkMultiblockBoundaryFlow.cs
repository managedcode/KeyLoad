using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockBoundaryFlow
{
    private const long CorrectionRevision = 259;
    private const long FinalCorrectionRevision = 261;

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new ChunkSelectedIntegrityFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(ChunkSelectedIntegrityFixture fixture)
    {
        fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            ChunkMultiblockBoundaryData.Additional, SampleChunkCanonicalFixture.Tags)));
        fixture.Apply(fixture.Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, CorrectionRevision)));
        await ChunkMultiblockBoundaryRows.RequireAsync(fixture, healthy: false);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        await FaultAsync(fixture);
        await ChunkMultiblockBoundaryRows.RequireAsync(fixture, healthy: false);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, [ChunkSelectedIntegrityFixture.Fresh], SampleChunkCanonicalFixture.Tags)));
        fixture.Apply(fixture.Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, FinalCorrectionRevision)));
        await ChunkMultiblockBoundaryRows.RequireAsync(fixture, healthy: true);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        for (var cold = 0; cold < 2; cold++)
        {
            fixture.Reopen();
            await ChunkMultiblockBoundaryRows.RequireAsync(fixture, healthy: true);
            await ChunkSelectedIntegrityAssertions.Replay(fixture);
        }
    }

    private static async Task FaultAsync(ChunkSelectedIntegrityFixture fixture)
    {
        var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var fault = await ChunkMultiblockBoundaryDamage.CaptureAsync(fixture);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            ChunkMultiblockBoundaryDamage.Inject(fixture, fault);
            var damaged = SampleRawIntegrityAssertions.Capture(fixture.Store);
            await NativeStoreAssertions.AdvancedAsync(fixture.Store, before.Position);
            await ChunkMultiblockBoundaryRefusal.RunAsync(fixture);
            fixture.Reopen();
            var cold = SampleRawIntegrityAssertions.Capture(fixture.Store);
            var position = await NativeStoreAssertions.ReopenedAsync(fixture.Store, damaged.Position);
            await Assert.That(cold.Position).IsEqualTo(position);
            await Assert.That(cold.Image).IsEqualTo(damaged.Image);
            await ChunkMultiblockBoundaryRefusal.RunAsync(fixture);
        }, failures);
        var repairCuts = new List<SampleRawIntegrityCut>();
        await ServerFailureObserver.ObserveAsync(() =>
        {
            repairCuts.Add(SampleRawIntegrityAssertions.Capture(fixture.Store));
            return Task.CompletedTask;
        }, failures);
        await ServerFailureObserver.ObserveAsync(() =>
        {
            ChunkMultiblockBoundaryDamage.Repair(fixture, fault);
            return Task.CompletedTask;
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var restored = SampleRawIntegrityAssertions.Capture(fixture.Store);
            await Assert.That(restored.Image).IsEqualTo(before.Image);
            foreach (var repairCut in repairCuts)
            {
                var position = await NativeStoreAssertions.AdvancedAsync(fixture.Store, repairCut.Position);
                await Assert.That(restored.Position).IsEqualTo(position);
                await Assert.That(restored.Identity.SequenceEqual(repairCut.Identity)).IsTrue();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
