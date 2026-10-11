using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobRetiredPartBytesDamage
{
    internal static BlobRetiredPartOwnedRow Apply(ZoneTreeStore store, BlobPriorPublicationState seed, BlobRetiredPartBytesFault fault)
    {
        var key = BlobPriorPublicationOracle.Key(fault == BlobRetiredPartBytesFault.AlteredRawBytes
            ? "blob-part-v1" : "blob-partmeta-v1", seed);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The acknowledged retired upload must own its selected part row.");
        store.Commit((tx, _) =>
        {
            if (fault == BlobRetiredPartBytesFault.AlteredRawBytes)
            { tx.Put(key, [5, 6, 7, 8]); }
            else
            { tx.PutRecord(key, new BlobPartMetadata(1, 3, BlobPriorPublicationOracle.Hash(new byte[] { 1, 2, 3, 4 }))); }
            return true;
        });
        return new(key, original);
    }

    internal static async Task VerifyAsync(ZoneTreeStore store, BlobRetiredPartOwnedRow row,
        BlobRetiredPartBytesFault fault, string[] originalImage, long originalPosition, byte[] identity)
    {
        var expected = fault == BlobRetiredPartBytesFault.AlteredRawBytes
            ? [5, 6, 7, 8]
            : NativeSerialization.Serialize(new BlobPartMetadata(1, 3,
                BlobPriorPublicationOracle.Hash(new byte[] { 1, 2, 3, 4 })));
        var prefix = Convert.ToHexString(row.Key) + ":";
        await Assert.That(originalImage.Count(value => value.StartsWith(prefix, StringComparison.Ordinal))).IsEqualTo(1);
        var image = originalImage.Select(value => value.StartsWith(prefix, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(expected) : value).ToArray();
        await BlobPriorPublicationImage.SameAsync(store, image, originalPosition + 1, identity);
    }
}
