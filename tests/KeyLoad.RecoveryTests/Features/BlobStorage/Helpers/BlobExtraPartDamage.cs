using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobExtraPartDamage
{
    internal static byte[] Apply(ZoneTreeStore store, BlobPriorPublicationState seed, BlobExtraPartFault fault)
    {
        var key = BlobLaterPartOracle.Key(fault == BlobExtraPartFault.ExtraRaw
            ? "blob-part-v1" : "blob-partmeta-v1", seed, 2);
        if (store.Read(view => view.ReadOwnedValue(key)) is not null)
        { throw new InvalidOperationException("The acknowledged two-part upload must not already own ordinal2."); }
        store.Commit((tx, _) =>
        {
            if (fault == BlobExtraPartFault.ExtraRaw)
            { tx.Put(key, [77, 78, 79]); }
            else
            { tx.PutRecord(key, new BlobPartMetadata(1, 3, BlobPriorPublicationOracle.Hash(new byte[] { 77, 78, 79 }))); }
            return true;
        });
        return key;
    }

    internal static async Task VerifyAsync(ZoneTreeStore store, byte[] key, BlobExtraPartFault fault,
        string[] originalImage, long position, byte[] identity)
    {
        var prefix = Convert.ToHexString(key) + ":";
        await Assert.That(originalImage.Any(value => value.StartsWith(prefix, StringComparison.Ordinal))).IsFalse();
        var expected = fault == BlobExtraPartFault.ExtraRaw ? [77, 78, 79]
            : NativeSerialization.Serialize(new BlobPartMetadata(1, 3,
                BlobPriorPublicationOracle.Hash(new byte[] { 77, 78, 79 })));
        var image = originalImage.Append(prefix + Convert.ToHexString(expected))
            .OrderBy(static value => value[..value.IndexOf(':', StringComparison.Ordinal)], StringComparer.Ordinal).ToArray();
        await BlobPriorPublicationImage.SameAsync(store, image, position + 1, identity);
    }
}
