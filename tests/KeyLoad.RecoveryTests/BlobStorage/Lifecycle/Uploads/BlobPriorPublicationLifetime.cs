using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobPriorPublicationLifetime
{
    internal static async Task<BlobPriorPublicationCommand> CleanAsync(DatabaseEngine owner, BlobPriorPublicationState seed,
        Command<DatabaseCommand> delete, Command<DatabaseCommand> reclaim)
    {
        var tombstone = await DeleteAsync(owner, seed, delete);
        var reclaimed = new BlobPriorPublicationCommand(reclaim, owner.Apply(reclaim));
        var result = reclaimed.Result.Get<BlobCommitResult<BlobReclaimResult>>();
        await BlobPriorPublicationImage.ExactAsync(result.Value, new BlobReclaimResult(seed.Upload, 1, 0, 4, true));
        await BlobPriorPublicationImage.ReceiptAsync(owner, result.Receipt, reclaim.CommandId, seed.Blob);
        await BlobPriorPublicationAssertions.QuotasAsync(owner, seed, 0, 0);
        foreach (var family in new[] { "blob-state-v1", "blob-part-v1", "blob-partmeta-v1" })
        {
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(BlobPriorPublicationOracle.Key(family, seed)))).IsNull();
        }
        await Assert.That(new BlobStorageOperations(owner).UploadInfo("root", new(seed.Blob, seed.Upload))).IsNull();
        await BlobPriorPublicationImage.ExactAsync(new BlobStorageOperations(owner).Metadata("root", new(seed.Blob)), tombstone);
        await BlobPriorPublicationImage.ReplayAsync(owner, reclaimed);
        return reclaimed;
    }

    private static async Task<BlobMetadata> DeleteAsync(DatabaseEngine owner, BlobPriorPublicationState seed, Command<DatabaseCommand> delete)
    {
        var deleted = new BlobPriorPublicationCommand(delete, owner.Apply(delete));
        var tombstone = seed.Metadata with
        {
            Revision = 2,
            VersionId = null,
            Length = 0,
            PartCount = 0,
            IntegrityHash = null,
            UpdatedAt = new DateTimeOffset(delete.Timestamp),
            Deleted = true
        };
        await BlobPriorPublicationImage.ExactAsync(deleted.Result.Get<BlobCommitResult<BlobMetadata>>().Value, tombstone);
        await BlobPriorPublicationImage.ReceiptAsync(owner, deleted.Result.Get<BlobCommitResult<BlobMetadata>>().Receipt, delete.CommandId, seed.Blob);
        await BlobPriorPublicationImage.ExactAsync(new BlobStorageOperations(owner).Metadata("root", new(seed.Blob)), tombstone);
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobState>(
            BlobPriorPublicationOracle.Key("blob-state-v1", seed))), seed.State with
            {
                Retired = true
            });
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobHead>(
            BlobPriorPublicationOracle.Key("blob-head-v1", seed))), new BlobHead(1, owner.Store.Identity.Incarnation, tombstone));
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(BlobPriorPublicationOracle.Key("blob-part-v1", seed)))!
            .AsSpan().SequenceEqual(seed.Bytes)).IsTrue();
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobPartMetadata>(
            BlobPriorPublicationOracle.Key("blob-partmeta-v1", seed))), new BlobPartMetadata(1, 4, BlobPriorPublicationOracle.Hash(seed.Bytes)));
        await BlobPriorPublicationAssertions.QuotasAsync(owner, seed, 4, 1);
        await BlobPriorPublicationImage.ReplayAsync(owner, deleted);
        return tombstone;
    }

    internal static async Task FinalAsync(DatabaseEngine owner, ZoneTreeStore store, BlobPriorPublicationState old,
        BlobPriorPublicationState current, BlobPriorPublicationCommand reclaimed)
    {
        var image = BlobPriorPublicationImage.Capture(store);
        var position = store.Position;
        await BlobPriorPublicationAssertions.HealthyAsync(owner, current);
        await BlobPriorPublicationRefusal.InvalidatedAsync(owner, old.Commands[0]);
        await BlobPriorPublicationRefusal.InvalidatedAsync(owner, old.Commands[2]);
        await BlobPriorPublicationRefusal.InvalidatedAsync(owner, reclaimed);
        await BlobPriorPublicationImage.SameAsync(store, image, position, old.Identity);
    }
}
