using KeyLoad.Core.TimeSeries;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class ChunkSelectedBlockIntegrityDamage
{
    internal static byte[] Key(ChunkSelectedIntegrityFixture fixture)
        => SampleChunkKeys.Block(fixture.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, 1, 0);

    internal static byte[] Inject(ChunkSelectedIntegrityFixture fixture, SampleChunkCorruptionCut fault)
    {
        var key = Key(fixture);
        var original = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        fixture.Store.Commit((tx, _) =>
        {
            if (fault == SampleChunkCorruptionCut.MissingBlock)
            {
                tx.Delete(key);
            }
            else if (fault == SampleChunkCorruptionCut.BlockDigest)
            {
                var changed = original.ToArray();
                changed[0] ^= 1;
                tx.Put(key, changed);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(fault));
            }
            return true;
        });
        return original;
    }

    internal static void Repair(ChunkSelectedIntegrityFixture fixture, byte[] original)
        => fixture.Store.Commit((tx, _) => { tx.Put(Key(fixture), original); return true; });
}
