using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobLaterPartDamage
{
    internal static BlobRetiredPartOwnedRow Apply(ZoneTreeStore store, BlobPriorPublicationState seed, BlobLaterPartFault fault)
    {
        var key = BlobLaterPartOracle.Key("blob-part-v1", seed, 1);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The acknowledged retired upload must own its second raw part row.");
        store.Commit((tx, _) =>
        {
            if (fault == BlobLaterPartFault.MissingSecondRaw)
            { tx.Delete(key); }
            else
            { tx.Put(key, [250, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66]); }
            return true;
        });
        return new(key, original);
    }

    internal static async Task VerifyAsync(ZoneTreeStore store, BlobRetiredPartOwnedRow row, BlobLaterPartFault fault,
        string[] originalImage, long position, byte[] identity)
    {
        var prefix = Convert.ToHexString(row.Key) + ":";
        await Assert.That(originalImage.Count(value => value.StartsWith(prefix, StringComparison.Ordinal))).IsEqualTo(1);
        byte[] altered = [250, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66];
        var image = fault == BlobLaterPartFault.MissingSecondRaw
            ? originalImage.Where(value => !value.StartsWith(prefix, StringComparison.Ordinal)).ToArray()
            : originalImage.Select(value => value.StartsWith(prefix, StringComparison.Ordinal)
                ? prefix + Convert.ToHexString(altered) : value).ToArray();
        await BlobPriorPublicationImage.SameAsync(store, image, position + 1, identity);
    }
}
