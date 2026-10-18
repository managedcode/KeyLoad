using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.RecoveryHost;
using KeyLoad.RecoveryTests.Features.Messaging;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobExtraPartTrial
{
    internal static async Task RunAsync(string root, BlobRetiredPartState seed, BlobExtraPartFault fault, CancellationToken token)
    {
        using var store = BlobPriorPublicationTrial.Open(root);
        var owner = QueueLifecycleRecoveryAssertions.Open(store);
        var queueOperation = JsonDefaults.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueLifecycleCrashProtocol.OperationFile), token));
        await Assert.That(OutcomeStoreOracle.Read(store, queueOperation)).IsNull();
        await QueueLifecycleRecoveryImage.AssertAsync(owner, store, committed: false);
        await BlobLaterPartAssertions.RetiredAsync(owner, seed);
        var request = new ReclaimBlobRequest(Guid.NewGuid(), seed.Publication.Blob, seed.Publication.Upload, 2);
        var original = CrashDatabase.Operation(owner, OperationKind.ReclaimBlob, request, request.CommandId);
        var image = BlobPriorPublicationImage.Capture(store);
        var position = store.Position;
        var key = BlobExtraPartDamage.Apply(store, seed.Publication, fault);
        await BlobExtraPartDamage.VerifyAsync(store, key, fault, image, position, seed.Publication.Identity);
        var damaged = BlobPriorPublicationImage.Capture(store);
        await BlobRetiredPartRefusal.AssertAsync(owner, store, seed, original, damaged, position + 1);
        await BlobRetiredPartRefusal.AssertAsync(owner, store, seed, original, damaged, position + 1);
        store.Dispose();
        using var cold = BlobPriorPublicationTrial.Open(root);
        var reopened = QueueLifecycleRecoveryAssertions.Open(cold);
        await BlobPriorPublicationImage.SameAsync(cold, damaged, position + 1, seed.Publication.Identity);
        await BlobRetiredPartRefusal.AssertAsync(reopened, cold, seed, original, damaged, position + 1);
        cold.Commit((tx, _) => { tx.Delete(key); return true; });
        await BlobPriorPublicationImage.SameAsync(cold, image, position + 2, seed.Publication.Identity);
        await BlobLaterPartAssertions.RetiredAsync(reopened, seed);
        var reclaimed = await BlobLaterPartReclaim.CompleteAsync(reopened, seed, original);
        var current = await BlobPriorPublicationCommands.PublishAsync(reopened, seed.Publication.Blob,
            seed.Publication.Upload, [9, 10, 11, 12], 2, seed.Publication.Identity, seed.Publication.Principal);
        await BlobLaterPartReclaim.FinalAsync(reopened, cold, seed, current, reclaimed);
        await ExtraAbsentAsync(reopened, seed.Publication);
        await QueueLifecycleRecoveryImage.AssertAsync(reopened, cold, committed: false);
        image = BlobPriorPublicationImage.Capture(cold);
        position = cold.Position;
        cold.Dispose();
        using var secondCold = BlobPriorPublicationTrial.Open(root);
        var final = QueueLifecycleRecoveryAssertions.Open(secondCold);
        await BlobPriorPublicationImage.SameAsync(secondCold, image, position, seed.Publication.Identity);
        await BlobLaterPartReclaim.FinalAsync(final, secondCold, seed, current, reclaimed);
        await ExtraAbsentAsync(final, seed.Publication);
        await Assert.That(OutcomeStoreOracle.Read(secondCold, queueOperation)).IsNull();
        await QueueLifecycleRecoveryImage.AssertAsync(final, secondCold, committed: false);
        await BlobPriorPublicationImage.SameAsync(secondCold, image, position, seed.Publication.Identity);
    }

    private static async Task ExtraAbsentAsync(DatabaseEngine owner, BlobPriorPublicationState seed)
    {
        foreach (var family in new[] { "blob-part-v1", "blob-partmeta-v1" })
        {
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(
            BlobLaterPartOracle.Key(family, seed, 2)))).IsNull();
        }
    }
}
