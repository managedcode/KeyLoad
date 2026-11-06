namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkSelectedIntegrityTrial
{
    internal static async Task Run(ChunkSelectedIntegrityFault fault)
    {
        using var fixture = new ChunkSelectedIntegrityFixture();
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false);
        var originalCut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var original = ChunkSelectedIntegrityDamage.Inject(fixture, fault);
        var damaged = SampleRawIntegrityAssertions.Capture(fixture.Store);
        await Assert.That(damaged.Position).IsGreaterThan(originalCut.Position);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        fixture.Reopen();
        damaged = await SampleRawIntegrityAssertions.ReopenedAsync(fixture.Store, damaged);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        ChunkSelectedIntegrityDamage.Put(fixture, original);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, originalCut with
        {
            Position = await NativeStoreAssertions.AdvancedAsync(fixture.Store, damaged.Position)
        });
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        fixture.Apply(fixture.Merge);
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false, generation: 2, revision: 6);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        fixture.Apply(fixture.Operation(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [ChunkSelectedIntegrityFixture.Fresh], SampleChunkCanonicalFixture.Tags)));
        fixture.Apply(fixture.Operation(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, 7)));
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: true);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        var healthy = SampleRawIntegrityAssertions.Capture(fixture.Store);
        fixture.Reopen();
        healthy = await SampleRawIntegrityAssertions.ReopenedAsync(fixture.Store, healthy);
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: true);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, healthy);
    }
}
