using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobRetiredPartReclaim
{
    internal static async Task<BlobPriorPublicationCommand> CompleteAsync(DatabaseEngine owner, BlobRetiredPartState seed,
        Command<DatabaseCommand> original)
    {
        var reclaimed = new BlobPriorPublicationCommand(original, owner.Apply(original));
        var result = reclaimed.Result.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobPriorPublicationImage.ExactAsync(result.Value, new BlobReclaimResult(seed.Publication.Upload, 1, 0, 4, true));
        await BlobPriorPublicationImage.ReceiptAsync(owner, result.Receipt, original.CommandId, seed.Publication.Blob);
        await BlobPriorPublicationAssertions.QuotasAsync(owner, seed.Publication, 0, 0);
        foreach (var family in new[] { "blob-state-v1", "blob-part-v1", "blob-partmeta-v1" })
        {
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(BlobPriorPublicationOracle.Key(family, seed.Publication)))).IsNull();
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
        var image = BlobPriorPublicationImage.Capture(store);
        var position = store.Position;
        await BlobPriorPublicationAssertions.HealthyAsync(owner, current);
        await BlobPriorPublicationImage.ReplayAsync(owner, old.Deletion);
        foreach (var command in old.Publication.Commands)
        {
            await BlobPriorPublicationRefusal.InvalidatedAsync(owner, command);
        }
        await BlobPriorPublicationRefusal.InvalidatedAsync(owner, reclaimed);
        await BlobPriorPublicationImage.SameAsync(store, image, position, old.Publication.Identity);
    }
}
