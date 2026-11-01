namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionIdentityContinuation
{
    internal static async Task RequireAsync(PartitionHostRecoveryFixture fixture, PartitionIdentityCut original)
    {
        await fixture.RunHostAsync(async recovered =>
        {
            var store = recovered.Database.Store;
            await StoreFileFixture.AssertPreservedIdentity(original.Identity, store.Identity).ConfigureAwait(false);
            await Assert.That(store.Identity.MinimumReaderContract).IsEqualTo(original.Identity.MinimumReaderContract);
            await Assert.That(store.Position).IsEqualTo(original.Position);
            await RequireValuesAsync(store, false).ConfigureAwait(false);
            store.Commit((transaction, _) =>
            {
                transaction.Put(PartitionIdentityProtocol.HealthyKey, PartitionIdentityProtocol.HealthyValue);
                return true;
            });
            await Assert.That(store.Position).IsEqualTo(original.Position + PartitionIdentityProtocol.NextCommit);
        }).ConfigureAwait(false);
        await fixture.RunHostAsync(async cold =>
        {
            await StoreFileFixture.AssertPreservedIdentity(original.Identity, cold.Database.Store.Identity).ConfigureAwait(false);
            await Assert.That(cold.Database.Store.Position)
                .IsEqualTo(original.Position + PartitionIdentityProtocol.NextCommit);
            await RequireValuesAsync(cold.Database.Store, true).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static async Task RequireValuesAsync(KeyLoad.Storage.IAtomicStore store, bool healthy)
    {
        await Assert.That(store.Read(view => view.ReadOwnedValue(PartitionIdentityProtocol.OriginalKey)))
            .IsEquivalentTo(PartitionIdentityProtocol.OriginalValue);
        var actual = store.Read(view => view.ReadOwnedValue(PartitionIdentityProtocol.HealthyKey));
        if (healthy)
        {
            await Assert.That(actual).IsEquivalentTo(PartitionIdentityProtocol.HealthyValue);
        }
        else
        {
            await Assert.That(actual).IsNull();
        }
    }
}
