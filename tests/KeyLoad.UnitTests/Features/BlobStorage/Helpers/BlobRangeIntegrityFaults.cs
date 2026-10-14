using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobRangeIntegrityFaults
{
    internal static async Task<BlobRangeOwnedFault> Install(ZoneTreeStore store,
        BlobRangeIntegrityFixture fixture, BlobRangeFault fault)
    {
        var metadata = fault == BlobRangeFault.UnsupportedPartMetadata;
        var key = BlobRangeIntegrityOracle.Key(metadata ? BlobRangeIntegrityFixture.PartMetadataSpace
            : BlobRangeIntegrityFixture.PartSpace, fixture, BlobRangeIntegrityFixture.SecondOrdinal);
        var original = store.Read(view => view.ReadOwnedValue(key))!;
        var tail = BlobRangeIntegrityFixture.Payload.AsSpan(BlobRangeIntegrityFixture.RawPartBytes).ToArray();
        var expected = metadata ? NativeSerialization.Serialize(new BlobPartMetadata(BlobRangeIntegrityFixture.FormatVersion,
            tail.Length, BlobRangeIntegrityOracle.Hash(tail))) : tail;
        await Assert.That(original.AsSpan().SequenceEqual(expected)).IsTrue();
        var damaged = Damage(fault, tail);
        store.Commit((tx, _) =>
        {
            if (damaged is null)
            {
                tx.Delete(key);
            }
            else
            {
                tx.Put(key, damaged);
            }
            return true;
        });
        return new(key, original);
    }

    internal static void Repair(ZoneTreeStore store, BlobRangeOwnedFault fault)
        => store.Commit((tx, _) => { tx.Put(fault.Key, fault.Original); return true; });

    private static byte[]? Damage(BlobRangeFault fault, byte[] tail)
    {
        switch (fault)
        {
            case BlobRangeFault.MissingRaw:
                return null;
            case BlobRangeFault.TruncatedRaw:
                return tail.AsSpan(BlobRangeIntegrityFixture.EmptyCount,
                    tail.Length - BlobRangeIntegrityFixture.SingleCount).ToArray();
            case BlobRangeFault.ChangedUnrequestedByte:
                tail[^BlobRangeIntegrityFixture.SingleCount] = BlobRangeIntegrityFixture.DamagedByte;
                return tail;
            case BlobRangeFault.UnsupportedPartMetadata:
                return NativeSerialization.Serialize(new BlobPartMetadata(BlobRangeIntegrityFixture.UnsupportedVersion,
                    tail.Length, BlobRangeIntegrityOracle.Hash(tail)));
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }
    }
}
