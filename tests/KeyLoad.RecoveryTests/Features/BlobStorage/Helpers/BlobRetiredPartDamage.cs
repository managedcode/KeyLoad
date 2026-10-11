using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobRetiredPartDamage
{
    internal static BlobRetiredPartOwnedRow Apply(ZoneTreeStore store, BlobPriorPublicationState seed, BlobRetiredPartFaultKind fault)
    {
        var key = BlobPriorPublicationOracle.Key(fault == BlobRetiredPartFaultKind.MissingRaw
            ? "blob-part-v1" : "blob-partmeta-v1", seed);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The acknowledged retired upload must own its selected part row.");
        store.Commit((tx, _) =>
        {
            if (fault == BlobRetiredPartFaultKind.WrongManifestHash)
            { tx.PutRecord(key, new BlobPartMetadata(1, 4, BlobPriorPublicationOracle.Hash(new byte[] { 5, 6, 7, 8 }))); }
            else
            { tx.Delete(key); }
            return true;
        });
        return new(key, original);
    }
}
