using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkCorrectionIdentityTrial
{
    internal static async Task Run()
    {
        using var fixture = new ChunkSelectedIntegrityFixture();
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: false);
        var originalCut = SampleRawIntegrityAssertions.Capture(fixture.Store);
        var original = await Inject(fixture);
        var damaged = SampleRawIntegrityAssertions.Capture(fixture.Store);
        await ChunkCorrectionIdentityAssertions.OnlyOwnedRowChanged(fixture, originalCut, damaged);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        fixture.Reopen();
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, damaged);
        await ChunkCorrectionIdentityAssertions.OnlyOwnedRowChanged(fixture, originalCut, damaged);
        await ChunkSelectedIntegrityDamage.Refuse(fixture);
        Put(fixture, original);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, originalCut with
        {
            Position = originalCut.Position + 2
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
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, healthy);
        await ChunkSelectedIntegrityAssertions.Healthy(fixture, final: true);
        await ChunkSelectedIntegrityAssertions.Replay(fixture);
        await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, healthy);
    }

    private static async Task<byte[]> Inject(ChunkSelectedIntegrityFixture fixture)
    {
        var original = fixture.Store.Read(view => view.ReadOwnedValue(Key(fixture)))!;
        var record = OrleansSerialization.Deserialize<SampleRecord>(original);
        await Assert.That(record.Sequence).IsEqualTo(3);
        await Assert.That(record.Sample.EventId).IsEqualTo("late");
        Put(fixture, OrleansSerialization.Serialize(record with
        {
            Sample = record.Sample with
            {
                EventId = "first"
            }
        }));
        return original;
    }

    internal static byte[] Key(ChunkSelectedIntegrityFixture fixture)
        => SampleChunkKeys.Correction(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, 3);

    private static void Put(ChunkSelectedIntegrityFixture fixture, byte[] bytes)
        => fixture.Store.Commit((tx, _) => { tx.Put(Key(fixture), bytes); return true; });
}
