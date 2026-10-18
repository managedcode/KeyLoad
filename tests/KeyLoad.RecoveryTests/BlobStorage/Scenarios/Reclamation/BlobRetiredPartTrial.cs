using ManagedCode.Communication.Commands;
using KeyLoad.RecoveryHost;
using KeyLoad.RecoveryTests.Features.Messaging;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobRetiredPartTrial
{
    internal static async Task RunAsync(string root, BlobRetiredPartState seed, BlobRetiredPartFaultKind fault, CancellationToken token)
    {
        using var store = BlobPriorPublicationTrial.Open(root);
        var owner = QueueLifecycleRecoveryAssertions.Open(store);
        var queueOperation = JsonDefaults.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueLifecycleCrashProtocol.OperationFile), token));
        await Assert.That(OutcomeStoreOracle.Read(store, queueOperation)).IsNull();
        await QueueLifecycleRecoveryImage.AssertAsync(owner, store, committed: false);
        await BlobRetiredPartAssertions.HealthyAsync(owner, seed);
        var request = new ReclaimBlobRequest(Guid.NewGuid(), seed.Publication.Blob, seed.Publication.Upload, 1);
        var original = CrashDatabase.Operation(owner, OperationKind.ReclaimBlob, request, request.CommandId);
        var image = BlobPriorPublicationImage.Capture(store);
        var position = store.Position;
        var row = BlobRetiredPartDamage.Apply(store, seed.Publication, fault);
        var damaged = BlobPriorPublicationImage.Capture(store);
        await BlobRetiredPartRefusal.AssertAsync(owner, store, seed, original, damaged, position + 1);
        await BlobRetiredPartRefusal.AssertAsync(owner, store, seed, original, damaged, position + 1);
        store.Dispose();
        using var cold = BlobPriorPublicationTrial.Open(root);
        var reopened = QueueLifecycleRecoveryAssertions.Open(cold);
        await BlobPriorPublicationImage.SameAsync(cold, damaged, position + 1, seed.Publication.Identity);
        await BlobRetiredPartRefusal.AssertAsync(reopened, cold, seed, original, damaged, position + 1);
        cold.Commit((tx, _) => { tx.Put(row.Key, row.Original); return true; });
        await BlobPriorPublicationImage.SameAsync(cold, image, position + 2, seed.Publication.Identity);
        await BlobRetiredPartAssertions.HealthyAsync(reopened, seed);
        var reclaimed = await BlobRetiredPartReclaim.CompleteAsync(reopened, seed, original);
        var current = await BlobPriorPublicationCommands.PublishAsync(reopened, seed.Publication.Blob,
            seed.Publication.Upload, [9, 10, 11, 12], 2, seed.Publication.Identity, seed.Publication.Principal);
        await BlobRetiredPartReclaim.FinalAsync(reopened, cold, seed, current, reclaimed);
        await QueueLifecycleRecoveryImage.AssertAsync(reopened, cold, committed: false);
        image = BlobPriorPublicationImage.Capture(cold);
        position = cold.Position;
        cold.Dispose();
        using var secondCold = BlobPriorPublicationTrial.Open(root);
        var final = QueueLifecycleRecoveryAssertions.Open(secondCold);
        await BlobPriorPublicationImage.SameAsync(secondCold, image, position, seed.Publication.Identity);
        await BlobRetiredPartReclaim.FinalAsync(final, secondCold, seed, current, reclaimed);
        await Assert.That(OutcomeStoreOracle.Read(secondCold, queueOperation)).IsNull();
        await QueueLifecycleRecoveryImage.AssertAsync(final, secondCold, committed: false);
        await BlobPriorPublicationImage.SameAsync(secondCold, image, position, seed.Publication.Identity);
    }
}
