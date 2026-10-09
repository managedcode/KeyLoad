using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionPairedIdentityContinuation
{
    internal static async Task RequireAsync(PartitionHostRecoveryFixture fixture, PartitionPairedIdentityCut original)
    {
        await using (var recovered = fixture.OpenHost())
        {
            var store = recovered.Database.Store;
            await WalFileFixture.AssertPreservedIdentity(original.Canonical, store.Identity).ConfigureAwait(false);
            await Assert.That(store.Identity.MinimumReaderContract).IsEqualTo(original.Canonical.MinimumReaderContract);
            await Assert.That(store.Position).IsEqualTo(original.CanonicalPosition);
            var journal = Path.Combine(fixture.Options.DataDirectory,
                PartitionHostRecoveryFixture.CanonicalDirectory, PartitionPairedIdentityProtocol.JournalFile);
            await Assert.That(new FileInfo(journal).Length).IsEqualTo(original.JournalLength);
            await RequireValuesAsync(store, false).ConfigureAwait(false);
            store.Commit((transaction, _) =>
            {
                transaction.Put(PartitionPairedIdentityProtocol.HealthyKey, PartitionPairedIdentityProtocol.HealthyValue);
                return true;
            });
            await Assert.That(store.Position).IsEqualTo(original.CanonicalPosition + PartitionPairedIdentityProtocol.NextFrame);
        }
        await using (var cold = fixture.OpenHost())
        {
            await WalFileFixture.AssertPreservedIdentity(original.Canonical, cold.Database.Store.Identity).ConfigureAwait(false);
            await Assert.That(cold.Database.Store.Position)
                .IsEqualTo(original.CanonicalPosition + PartitionPairedIdentityProtocol.NextFrame);
            await RequireValuesAsync(cold.Database.Store, true).ConfigureAwait(false);
        }
        var runtime = ServerRuntimeTestOptions.Runtime(fixture.Options);
        using var stores = new PartitionStores(runtime.Node, fixture.Options.DataDirectory,
            runtime.StorageExecution, runtime.PointCache);
        await WalFileFixture.AssertPreservedIdentity(original.Replica, stores.Replica.Identity).ConfigureAwait(false);
        await Assert.That(stores.Replica.Identity.MinimumReaderContract).IsEqualTo(original.Replica.MinimumReaderContract);
        await Assert.That(stores.Replica.Position).IsEqualTo(original.ReplicaPosition);
    }

    private static async Task RequireValuesAsync(KeyLoad.Storage.IAtomicStore store, bool healthy)
    {
        await Assert.That(store.Read(view => view.ReadOwnedValue(PartitionPairedIdentityProtocol.OriginalKey)))
            .IsEquivalentTo(PartitionPairedIdentityProtocol.OriginalValue);
        await Assert.That(store.Read(view => view.ReadOwnedValue(PartitionPairedIdentityProtocol.TornKey))).IsNull();
        var actual = store.Read(view => view.ReadOwnedValue(PartitionPairedIdentityProtocol.HealthyKey));
        if (healthy)
        {
            await Assert.That(actual).IsEquivalentTo(PartitionPairedIdentityProtocol.HealthyValue);
        }
        else
        {
            await Assert.That(actual).IsNull();
        }
    }
}
