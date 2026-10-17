using KeyLoad.RecoveryTests.Features.Messaging;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobPriorPublicationTrial
{
    internal static ZoneTreeStore Open(string root) => new(new(root),
        RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());

    internal static async Task RunAsync(string root, BlobPriorPublicationState seed, bool missing, CancellationToken token)
    {
        using var store = Open(root);
        var owner = QueueLifecycleRecoveryAssertions.Open(store);
        var fault = await BlobPriorPublicationDamage.ApplyAsync(root, owner, store, seed, missing, token);
        store.Dispose();
        using var cold = Open(root);
        var reopened = QueueLifecycleRecoveryAssertions.Open(cold);
        await BlobPriorPublicationImage.SameAsync(cold, fault.DamagedImage, fault.Position + 1, seed.Identity);
        await BlobPriorPublicationRefusal.AssertAsync(reopened, cold, seed, fault.Delete, fault.Reclaim, fault.DamagedImage, fault.Position + 1);
        cold.Commit((tx, _) => { tx.Put(fault.Key, fault.OriginalRow); return true; });
        await BlobPriorPublicationImage.SameAsync(cold, fault.OriginalImage, fault.Position + 2, seed.Identity);
        await BlobPriorPublicationAssertions.HealthyAsync(reopened, seed);
        var reclaimed = await BlobPriorPublicationLifetime.CleanAsync(reopened, seed, fault.Delete, fault.Reclaim);
        var current = await BlobPriorPublicationCommands.PublishAsync(reopened, seed.Blob, seed.Upload,
            [5, 6, 7, 8], 2, seed.Identity, seed.Catalog, seed.Principal);
        await BlobPriorPublicationLifetime.FinalAsync(reopened, cold, seed, current, reclaimed);
        await QueueLifecycleRecoveryImage.AssertAsync(reopened, cold, committed: false);
        var image = BlobPriorPublicationImage.Capture(cold);
        var position = cold.Position;
        cold.Dispose();
        using var secondCold = Open(root);
        var final = QueueLifecycleRecoveryAssertions.Open(secondCold);
        await BlobPriorPublicationImage.SameAsync(secondCold, image, position, seed.Identity);
        await BlobPriorPublicationLifetime.FinalAsync(final, secondCold, seed, current, reclaimed);
        await Assert.That(OutcomeStoreOracle.Read(secondCold, fault.QueueOperation)).IsNull();
        await QueueLifecycleRecoveryImage.AssertAsync(final, secondCold, committed: false);
        await BlobPriorPublicationImage.SameAsync(secondCold, image, position, seed.Identity);
    }
}
