using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobPriorPublicationDamage
{
    internal static async Task<BlobPriorPublicationFault> ApplyAsync(string root, DatabaseEngine owner,
        ZoneTreeStore store, BlobPriorPublicationState seed, bool missing, CancellationToken token)
    {
        var queueOperation = JsonDefaults.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueLifecycleCrashProtocol.OperationFile), token));
        await Assert.That(OutcomeStoreOracle.Read(store, queueOperation)).IsNull();
        await QueueLifecycleRecoveryImage.AssertAsync(owner, store, committed: false);
        await BlobPriorPublicationAssertions.HealthyAsync(owner, seed);
        var deleteRequest = new DeleteBlobRequest(Guid.NewGuid(), seed.Blob, 1);
        var delete = CrashDatabase.Operation(OperationKind.DeleteBlob, deleteRequest, deleteRequest.CommandId);
        var reclaimRequest = new ReclaimBlobRequest(Guid.NewGuid(), seed.Blob, seed.Upload, 1);
        var reclaim = CrashDatabase.Operation(OperationKind.ReclaimBlob, reclaimRequest, reclaimRequest.CommandId);
        var image = BlobPriorPublicationImage.Capture(store);
        var position = store.Position;
        var key = BlobPriorPublicationOracle.Key("blob-state-v1", seed);
        var originalRow = store.Read(view => view.ReadOwnedValue(key))!;
        store.Commit((tx, _) =>
        {
            if (missing)
            { tx.Delete(key); }
            else
            { tx.PutRecord(key, seed.State with { Retired = true }); }
            return true;
        });
        var damaged = BlobPriorPublicationImage.Capture(store);
        await BlobPriorPublicationRefusal.AssertAsync(owner, store, seed, delete, reclaim, damaged, position + 1);
        await BlobPriorPublicationRefusal.AssertAsync(owner, store, seed, delete, reclaim, damaged, position + 1);
        return new(delete, reclaim, queueOperation, image, damaged, position, key, originalRow);
    }
}
