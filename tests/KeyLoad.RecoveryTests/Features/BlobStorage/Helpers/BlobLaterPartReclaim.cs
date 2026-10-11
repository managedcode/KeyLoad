using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobLaterPartReclaim
{
    internal static async Task<BlobPriorPublicationCommand> CompleteAsync(DatabaseEngine owner,
        BlobRetiredPartState seed, ReplicatedOperation original)
    {
        var reclaimed = new BlobPriorPublicationCommand(original, owner.Apply(original));
        var result = reclaimed.Result.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobPriorPublicationImage.ExactAsync(result.Value, new BlobReclaimResult(seed.Publication.Upload, 2, 0, 65_553, true));
        await BlobPriorPublicationImage.ReceiptAsync(owner, result.Receipt, original.Id, seed.Publication.Blob);
        await BlobPriorPublicationAssertions.QuotasAsync(owner, seed.Publication, 0, 0);
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(
            BlobPriorPublicationOracle.Key("blob-state-v1", seed.Publication)))).IsNull();
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            foreach (var family in new[] { "blob-part-v1", "blob-partmeta-v1" })
            {
                await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(
                BlobLaterPartOracle.Key(family, seed.Publication, ordinal)))).IsNull();
            }
        }
        await Assert.That(new BlobStorageOperations(owner).UploadInfo("root", new(seed.Publication.Blob, seed.Publication.Upload))).IsNull();
        await BlobRetiredPartAssertions.TombstoneAsync(owner, seed);
        await BlobPriorPublicationImage.ReplayAsync(owner, seed.Deletion);
        await BlobPriorPublicationImage.ReplayAsync(owner, reclaimed);
        return reclaimed;
    }

    internal static async Task FinalAsync(DatabaseEngine owner, ZoneTreeStore store, BlobRetiredPartState old,
        BlobPriorPublicationState current, BlobPriorPublicationCommand reclaimed)
    {
        await BlobRetiredPartReclaim.FinalAsync(owner, store, old, current, reclaimed);
        foreach (var family in new[] { "blob-part-v1", "blob-partmeta-v1" })
        {
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(
            BlobLaterPartOracle.Key(family, old.Publication, 1)))).IsNull();
        }
    }
}
