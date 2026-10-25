using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkMultiblockBoundaryFlow
{
    private const long CorrectionRevision = 259;
    private const long FinalCorrectionRevision = 261;
    private const long FaultAndRepairCommits = 2;

    internal static async Task RunAsync(bool unknownVersion)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new ChunkSelectedIntegrityFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture, unknownVersion), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(ChunkSelectedIntegrityFixture fixture, bool unknownVersion)
    {
        fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            ChunkMultiblockBoundaryData.Additional, SampleChunkCanonicalFixture.Tags)));
        fixture.Apply(fixture.Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, CorrectionRevision)));
        await ChunkMultiblockBoundaryRows.RequireAsync(fixture, healthy: false);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        await FaultAsync(fixture, unknownVersion);
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

    private static async Task FaultAsync(ChunkSelectedIntegrityFixture fixture, bool unknownVersion)
    {
        var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var fault = await ChunkMultiblockBoundaryDamage.CaptureAsync(fixture);
        var failures = new List<Exception>();
        var injected = new List<long>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            ChunkMultiblockBoundaryDamage.Inject(fixture, fault, unknownVersion);
            injected.Add(before.Position);
            var damaged = SampleRawIntegrityAssertions.Capture(fixture.Store);
            await Assert.That(damaged.Position).IsEqualTo(checked(before.Position + 1));
            await ChunkMultiblockBoundaryRefusal.RunAsync(fixture, unknownVersion);
            fixture.Reopen();
            var cold = SampleRawIntegrityAssertions.Capture(fixture.Store);
            await Assert.That(cold.Position).IsEqualTo(damaged.Position);
            await Assert.That(cold.Image).IsEqualTo(damaged.Image);
            await ChunkMultiblockBoundaryRefusal.RunAsync(fixture, unknownVersion);
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
            foreach (var position in injected)
            {
                await Assert.That(restored.Position).IsEqualTo(checked(position + FaultAndRepairCommits));
            }
            foreach (var repairCut in repairCuts)
            {
                await Assert.That(restored.Position).IsEqualTo(checked(repairCut.Position + 1));
                await Assert.That(restored.Identity.SequenceEqual(repairCut.Identity)).IsTrue();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
