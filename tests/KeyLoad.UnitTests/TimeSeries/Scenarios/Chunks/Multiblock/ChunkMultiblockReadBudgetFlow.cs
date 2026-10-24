using KeyLoad.Server;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class ChunkMultiblockReadBudgetFlow
{
    private const long InitialCorrectionRevision = 259;
    private const long HealthyCorrectionRevision = 261;
    private const int ColdOwners = 2;

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
        fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, ChunkMultiblockBoundaryData.Additional, SampleChunkCanonicalFixture.Tags)));
        fixture.Apply(fixture.Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, InitialCorrectionRevision)));
        await ChunkMultiblockBoundaryRows.RequireAsync(fixture, healthy: false);
        await ChunkMultiblockReadBudgetAssertions.RequireAsync(fixture, healthy: false);
        fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, [ChunkSelectedIntegrityFixture.Fresh], SampleChunkCanonicalFixture.Tags)));
        fixture.Apply(fixture.Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, HealthyCorrectionRevision)));
        await ChunkMultiblockReadBudgetAssertions.RequireAsync(fixture, healthy: true);
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
            fixture.Reopen();
            var after = SampleRawIntegrityAssertions.Capture(fixture.Store);
            await Assert.That(after.Image).IsEqualTo(before.Image);
            await Assert.That(after.Position).IsEqualTo(before.Position);
            await ChunkMultiblockReadBudgetAssertions.RequireAsync(fixture, healthy: true);
        }
    }
}
